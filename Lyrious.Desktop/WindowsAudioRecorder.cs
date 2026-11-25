using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Lyrious.UILib;
using Lyrious.UILib.Localization;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Lyrious.Desktop;

/// <summary>
/// Records from Windows's default sound input (Settings › Sound › Input: a microphone, or a sound
/// card's input) through WASAPI, as the computer has it set up (its own sample rate). Its first two
/// channels are handed on as 16-bit samples, interleaved; a one-channel input is given in both.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAudioRecorder : IAudioRecorder
{
    private WasapiCapture? capture;
    private TaskCompletionSource? stopped;

    /// <summary>The computer's sound inputs that are connected, by their names in Windows.</summary>
    public Task<IReadOnlyList<AudioInput>> GetInputsAsync()
    {
        using var devices = new MMDeviceEnumerator();
        var inputs = new List<AudioInput>();
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            inputs.Add(new AudioInput(device.ID, device.FriendlyName));
            device.Dispose();
        }

        return Task.FromResult<IReadOnlyList<AudioInput>>(inputs);
    }

    public Task<int> StartAsync(string? inputId, Action<short[], int> samples)
    {
        WasapiCapture started;
        try
        {
            started = Chosen(inputId) is { } device ? new WasapiCapture(device) : new WasapiCapture();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            throw new InvalidOperationException(L.T("This computer has no sound input to record from (Windows' Sound settings, Input)."), ex);
        }

        var format = started.WaveFormat;
        var read = Reader(format);
        var channels = format.Channels;
        var frameSize = format.BlockAlign;
        var buffer = Array.Empty<short>();
        started.DataAvailable += (_, e) =>
        {
            var frames = e.BytesRecorded / frameSize;
            if (buffer.Length < frames * 2)
            {
                buffer = new short[frames * 2];
            }

            for (var frame = 0; frame < frames; frame++)
            {
                var at = frame * frameSize;
                var left = read(e.Buffer, at);
                buffer[2 * frame] = left;
                buffer[2 * frame + 1] = channels > 1 ? read(e.Buffer, at + format.BitsPerSample / 8) : left;
            }

            samples(buffer, frames * 2);
        };

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        started.RecordingStopped += (_, _) => done.TrySetResult();
        started.StartRecording();
        capture = started;
        stopped = done;
        return Task.FromResult(format.SampleRate);
    }

    public async Task StopAsync()
    {
        if (capture is not { } recording)
        {
            return;
        }

        capture = null;
        recording.StopRecording();
        // The last sound recorded is handed over before it says it has stopped.
        await (stopped?.Task ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { });
        recording.Dispose();
    }

    /// <summary>The input chosen in Settings, if it's connected; else null (Windows' default input is used).</summary>
    private static MMDevice? Chosen(string? inputId)
    {
        if (string.IsNullOrEmpty(inputId))
        {
            return null;
        }

        try
        {
            using var devices = new MMDeviceEnumerator();
            var device = devices.GetDevice(inputId);
            return device.State == DeviceState.Active ? device : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads one sample at a byte offset as 16 bits, from what Windows records (float, or 16, 24 or 32-bit whole numbers).</summary>
    private static Func<byte[], int, short> Reader(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible extensible && extensible.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        return (isFloat, format.BitsPerSample) switch
        {
            (true, 32) => (data, at) => (short)(Math.Clamp(BitConverter.ToSingle(data, at), -1f, 1f) * short.MaxValue),
            (false, 16) => (data, at) => BitConverter.ToInt16(data, at),
            (false, 24) => (data, at) => (short)(data[at + 1] | (data[at + 2] << 8)),
            (false, 32) => (data, at) => (short)(BitConverter.ToInt32(data, at) >> 16),
            _ => throw new InvalidOperationException(L.T("The computer's sound input records in a way Lyrious can't read ({0}).", format)),
        };
    }
}
