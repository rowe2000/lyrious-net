using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lyrious.UILib;
using NAudio.Wave;
using SoundTouch;

namespace Lyrious.Desktop;

/// <summary>
/// Sound files for songs' media links, in the "sounds" folder next to the settings (in the user's
/// application data folder). On Windows they're played in the app (NAudio), through SoundTouch,
/// so the speed and the pitch can each be changed without the other. Elsewhere, and for a file
/// Windows can't decode, the computer's own player for the file type plays it.
/// </summary>
internal sealed class FileSoundStore : ISoundStore
{
    private readonly string folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lyrious", "sounds");

    // Playback runs on its own thread: the settings and the playing sound are changed under this.
    private readonly object gate = new();
    private WaveOutEvent? output;
    private MediaFoundationReader? reader;
    private Stretched? stretch;
    private string? playing;
    private double speed = 1;
    private double pitch;

    public string? Playing
    {
        get
        {
            lock (gate)
            {
                return playing;
            }
        }
    }

    public event Action? PlayingChanged;

    public bool CanChangeSpeed => OperatingSystem.IsWindows();

    public bool CanChangePitch => OperatingSystem.IsWindows();

    public double Speed
    {
        get => speed;
        set
        {
            lock (gate)
            {
                speed = Math.Clamp(value, 0.25, 4);
                if (stretch != null)
                {
                    stretch.Processor.Tempo = speed;
                }
            }
        }
    }

    public double Pitch
    {
        get => pitch;
        set
        {
            lock (gate)
            {
                pitch = Math.Clamp(value, -24, 24);
                if (stretch != null)
                {
                    stretch.Processor.PitchSemiTones = pitch;
                }
            }
        }
    }

    public async Task SaveAsync(string id, Stream content)
    {
        Directory.CreateDirectory(folder);
        var path = PathOf(id);
        // Written next to it first, so a failure halfway leaves no half a file under the id.
        var partial = path + ".part";
        try
        {
            await using (var file = File.Create(partial))
            {
                await content.CopyToAsync(file);
            }

            File.Move(partial, path, overwrite: true);
        }
        catch (IOException ex)
        {
            File.Delete(partial);
            throw new IOException($"It couldn't be written to {folder}. {ex.Message}", ex);
        }
    }

    public Task<Stream?> OpenAsync(string id)
    {
        var path = PathOf(id);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task<IReadOnlyList<StoredSound>> ListAsync()
    {
        IReadOnlyList<StoredSound> sounds = Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles()
                .Where(file => !file.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .Select(file => new StoredSound(file.Name, file.Length))
                .ToList()
            : [];
        return Task.FromResult(sounds);
    }

    public Task DeleteAsync(string id)
    {
        if (Playing == id)
        {
            Stop();
        }

        File.Delete(PathOf(id));
        return Task.CompletedTask;
    }

    public Task<bool> PlayAsync(string id)
    {
        var path = PathOf(id);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        Stop();
        if (OperatingSystem.IsWindows() && TryPlayHere(id, path))
        {
            return Task.FromResult(true);
        }

        // The player the computer opens this kind of file with.
        using var player = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.FromResult(true);
    }

    /// <summary>Plays the file in the app; false when Windows can't decode it (e.g. Ogg).</summary>
    private bool TryPlayHere(string id, string path)
    {
        MediaFoundationReader? opened = null;
        WaveOutEvent? device = null;
        try
        {
            opened = new MediaFoundationReader(path);
            var stretched = new Stretched(opened.ToSampleProvider(), gate);
            device = new WaveOutEvent { DesiredLatency = 200 };
            lock (gate)
            {
                stretched.Processor.Tempo = speed;
                stretched.Processor.PitchSemiTones = pitch;
                device.Init(stretched);
                reader = opened;
                stretch = stretched;
                output = device;
                playing = id;
            }

            device.PlaybackStopped += (_, _) =>
            {
                // Played to the end (or stopped): only for the sound that's still the one playing.
                if (Release(device))
                {
                    PlayingChanged?.Invoke();
                }
            };
            device.Play();
            PlayingChanged?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine($"Couldn't play the sound in the app: {ex.Message}");
            if (device != null && Release(device))
            {
                PlayingChanged?.Invoke();
            }
            else
            {
                device?.Dispose();
                opened?.Dispose();
            }

            return false;
        }
    }

    public void Stop()
    {
        WaveOutEvent? device;
        lock (gate)
        {
            device = output;
        }

        // Raises PlaybackStopped, which releases it.
        device?.Stop();
        if (device != null && Release(device))
        {
            PlayingChanged?.Invoke();
        }
    }

    /// <summary>Lets go of the playback if it's still this one's; true when it was.</summary>
    private bool Release(WaveOutEvent device)
    {
        MediaFoundationReader? file;
        lock (gate)
        {
            if (output != device)
            {
                return false;
            }

            file = reader;
            output = null;
            reader = null;
            stretch = null;
            playing = null;
        }

        device.Dispose();
        file?.Dispose();
        return true;
    }

    /// <summary>The file of a sound: just its name in the folder, whatever the id says.</summary>
    private string PathOf(string id) => Path.Combine(folder, Path.GetFileName(id));

    /// <summary>
    /// The sound through SoundTouch, which changes its tempo and its pitch each without the other
    /// (<see cref="Processor"/>). Read under the lock, so the speed and pitch aren't changed halfway
    /// through a read (they're set under it too).
    /// </summary>
    private sealed class Stretched : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly object gate;
        private readonly int channels;
        private readonly float[] input;
        private bool ended;

        public Stretched(ISampleProvider source, object gate)
        {
            this.source = source;
            this.gate = gate;
            channels = source.WaveFormat.Channels;
            input = new float[4096 * channels];
            Processor = new SoundTouchProcessor { Channels = channels, SampleRate = source.WaveFormat.SampleRate };
        }

        public SoundTouchProcessor Processor { get; }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            lock (gate)
            {
                var written = 0;
                // Whole frames (a sample for each channel) only.
                count -= count % channels;
                while (written < count)
                {
                    var frames = Processor.ReceiveSamples(buffer.AsSpan(offset + written, count - written), (count - written) / channels);
                    written += frames * channels;
                    if (written >= count || (ended && frames == 0))
                    {
                        break;
                    }

                    if (ended)
                    {
                        continue;
                    }

                    // More of the file for SoundTouch; at its end, what SoundTouch still holds comes out.
                    var read = source.Read(input, 0, input.Length);
                    read -= read % channels;
                    if (read == 0)
                    {
                        Processor.Flush();
                        ended = true;
                    }
                    else
                    {
                        Processor.PutSamples(input.AsSpan(0, read), read / channels);
                    }
                }

                return written;
            }
        }
    }
}
