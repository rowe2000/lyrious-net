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
/// application data folder). They're played in the app, so the speed and the pitch can each be
/// changed without the other: on Windows through NAudio and SoundTouch, on macOS through its audio
/// engine (<see cref="MacSoundPlayer"/>). An Ogg Opus file (as the app records the band) is decoded by
/// the app itself, which neither computer can do. Elsewhere, and for a file the computer can't decode,
/// the computer's own player for the file type plays it.
/// </summary>
internal sealed class FileSoundStore : ISoundStore
{
    private readonly string folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lyrious", "sounds");

    // Playback runs on its own thread: the settings and the playing sound are changed under this.
    private readonly object gate = new();
    private WaveOutEvent? output;
    private WaveStream? reader;
    private Stretched? stretch;
    private MacSoundPlayer? mac;
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

    public bool CanChangeSpeed => OperatingSystem.IsWindows() || (OperatingSystem.IsMacOS() && MacSoundPlayer.IsAvailable);

    public bool CanChangePitch => CanChangeSpeed;

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

                if (OperatingSystem.IsMacOS())
                {
                    mac?.SetSpeed(speed);
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

                if (OperatingSystem.IsMacOS())
                {
                    mac?.SetPitch(pitch);
                }
            }
        }
    }

    public TimeSpan? Position
    {
        get
        {
            MacSoundPlayer? macSound;
            lock (gate)
            {
                if (reader != null)
                {
                    return reader.CurrentTime;
                }

                macSound = mac;
            }

            return OperatingSystem.IsMacOS() ? macSound?.Position : null;
        }
    }

    public TimeSpan? Duration
    {
        get
        {
            lock (gate)
            {
                if (reader != null)
                {
                    return reader.TotalTime;
                }

                return OperatingSystem.IsMacOS() ? mac?.Duration : null;
            }
        }
    }

    public void Seek(TimeSpan position)
    {
        MacSoundPlayer? macSound;
        lock (gate)
        {
            if (reader != null && stretch != null)
            {
                // Read under the lock too: SoundTouch drops what it holds of the old place.
                reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero : position > reader.TotalTime ? reader.TotalTime : position;
                stretch.Restart();
                return;
            }

            macSound = mac;
        }

        if (OperatingSystem.IsMacOS())
        {
            macSound?.Seek(position);
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

        if (OperatingSystem.IsMacOS() && MacSoundPlayer.IsAvailable && TryPlayOnMac(id, IsOggOpus(path) ? DecodedForMac(id, path) ?? path : path))
        {
            return Task.FromResult(true);
        }

        // The player the computer opens this kind of file with.
        using var player = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.FromResult(true);
    }

    /// <summary>Plays the file in the app; false when it can't be decoded (by Windows, or for Ogg Opus by the app).</summary>
    private bool TryPlayHere(string id, string path)
    {
        WaveStream? opened = null;
        WaveOutEvent? device = null;
        try
        {
            opened = IsOggOpus(path) ? new OggOpusReader(path) : new MediaFoundationReader(path);
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

    /// <summary>Plays the file in the app on macOS; false when macOS can't decode it.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private bool TryPlayOnMac(string id, string path)
    {
        try
        {
            MacSoundPlayer sound;
            lock (gate)
            {
                sound = MacSoundPlayer.Play(path, speed, pitch);
                mac = sound;
                playing = id;
            }

            sound.Ended += () =>
            {
                // Played to the end: only for the sound that's still the one playing.
                if (ReleaseMac(sound))
                {
                    PlayingChanged?.Invoke();
                }
            };
            PlayingChanged?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine($"Couldn't play the sound in the app: {ex.Message}");
            return false;
        }
    }

    /// <summary>Lets go of the macOS sound if it's still the one playing; true when it was.</summary>
    private bool ReleaseMac(MacSoundPlayer sound)
    {
        lock (gate)
        {
            if (mac != sound)
            {
                return false;
            }

            mac = null;
            playing = null;
        }

        if (OperatingSystem.IsMacOS())
        {
            sound.Dispose();
        }

        return true;
    }

    public void Stop()
    {
        WaveOutEvent? device;
        MacSoundPlayer? macSound;
        lock (gate)
        {
            device = output;
            macSound = mac;
        }

        if (macSound != null && ReleaseMac(macSound))
        {
            PlayingChanged?.Invoke();
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
        WaveStream? file;
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

    /// <summary>Whether the file is Ogg (as the app records, in Opus), by its name.</summary>
    private static bool IsOggOpus(string path)
        => path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".oga", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An Ogg Opus file decoded into a WAV file macOS plays (it can't decode Ogg itself), in the
    /// computer's temporary folder; made once per sound. Null when it can't be decoded.
    /// </summary>
    private static string? DecodedForMac(string id, string path)
    {
        var decoded = Path.Combine(Path.GetTempPath(), "Lyrious", Path.GetFileNameWithoutExtension(Path.GetFileName(id)) + ".wav");
        try
        {
            if (!File.Exists(decoded) || File.GetLastWriteTimeUtc(decoded) < File.GetLastWriteTimeUtc(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(decoded)!);
                using var reader = new OggOpusReader(path);
                WaveFileWriter.CreateWaveFile(decoded, reader);
            }

            return decoded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't decode the sound {id}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// An Ogg Opus file (as the app records the band) decoded by the app itself (Concentus), as 16-bit
    /// stereo at 48 kHz (a one-channel file in both channels), from where it's moved to.
    /// </summary>
    private sealed class OggOpusReader : WaveStream
    {
        private const int SampleRate = 48000;
        private readonly Stream file;
        private Concentus.IOpusDecoder decoder;
        private Concentus.Oggfile.OpusOggReadStream ogg;
        // What's decoded but not read yet: the rest of the last packet.
        private short[] decoded = [];
        private int decodedAt;
        private long position;

        public OggOpusReader(string path)
        {
            file = File.OpenRead(path);
            try
            {
                decoder = Concentus.OpusCodecFactory.CreateDecoder(SampleRate, 2);
                ogg = new Concentus.Oggfile.OpusOggReadStream(decoder, file);
                if (!ogg.HasNextPacket)
                {
                    throw new InvalidDataException(ogg.LastError ?? "It isn't an Ogg Opus file.");
                }
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        public override WaveFormat WaveFormat { get; } = new(SampleRate, 16, 2);

        public override long Length => BlockAligned(ogg.TotalTime.TotalSeconds * WaveFormat.AverageBytesPerSecond);

        public override long Position
        {
            get => position;
            set
            {
                var at = Math.Clamp(BlockAligned(value), 0, Length);
                // Concentus' own seeking doesn't move: the file is read again from its start instead,
                // skipping whole packets (not decoded, so it's quick) up to the one with the place.
                file.Position = 0;
                decoder = Concentus.OpusCodecFactory.CreateDecoder(SampleRate, 2);
                ogg = new Concentus.Oggfile.OpusOggReadStream(decoder, file);
                decoded = [];
                decodedAt = 0;
                var target = at / WaveFormat.BlockAlign;
                long skipped = 0;
                while (ogg.HasNextPacket && ogg.ReadNextRawPacket() is { } packet)
                {
                    var samples = Concentus.Structs.OpusPacketInfo.GetNumSamples(packet, SampleRate);
                    if (samples <= 0)
                    {
                        continue;
                    }

                    if (skipped + samples > target)
                    {
                        // This packet has the place: decoded, and read from the place on.
                        var output = new short[samples * 2];
                        var frames = decoder.Decode(packet, output, samples, false);
                        decoded = output[..(Math.Max(0, frames) * 2)];
                        decodedAt = (int)Math.Min(decoded.Length, (target - skipped) * 2);
                        break;
                    }

                    skipped += samples;
                }

                position = at;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var written = 0;
            var wanted = count - count % 2;
            while (written < wanted)
            {
                if (decodedAt >= decoded.Length)
                {
                    if (!ogg.HasNextPacket)
                    {
                        break;
                    }

                    // (A packet that doesn't decode is skipped.)
                    decoded = ogg.DecodeNextPacket() ?? [];
                    decodedAt = 0;
                    continue;
                }

                var samples = Math.Min(decoded.Length - decodedAt, (wanted - written) / 2);
                Buffer.BlockCopy(decoded, decodedAt * 2, buffer, offset + written, samples * 2);
                decodedAt += samples;
                written += samples * 2;
            }

            position += written;
            return written;
        }

        /// <summary>A place in bytes on a whole sample of both channels.</summary>
        private long BlockAligned(double bytes) => (long)bytes / WaveFormat.BlockAlign * WaveFormat.BlockAlign;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>The file of a sound: just its name in the folder, whatever the id says.</summary>
    private string PathOf(string id) => Path.Combine(folder, Path.GetFileName(id));

    /// <summary>
    /// How loud the sound is over time: an Ogg Opus file (as the app records) decoded by the app
    /// itself, anything else by the computer (Media Foundation on Windows, AVFoundation on macOS).
    /// </summary>
    public Task<SoundWave?> ReadWaveAsync(string id) => Task.Run(() =>
    {
        var path = PathOf(id);
        if (!File.Exists(path))
        {
            return null;
        }

        if (IsOggOpus(path))
        {
            using var ogg = File.OpenRead(path);
            if (SoundWave.FromOggOpus(ogg) is { } wave)
            {
                return wave;
            }
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacSoundPlayer.ReadWave(path);
            }

            if (OperatingSystem.IsWindows())
            {
                using var reader = new MediaFoundationReader(path);
                var samples = reader.ToSampleProvider();
                var builder = new SoundWaveBuilder(samples.WaveFormat.SampleRate, samples.WaveFormat.Channels);
                var buffer = new float[16384];
                int read;
                while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var i = 0; i < read; i++)
                    {
                        builder.Add(buffer[i]);
                    }
                }

                return builder.Build();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Couldn't read the sound {id}: {ex.Message}");
        }

        return (SoundWave?)null;
    });

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

        /// <summary>After the file moved (a seek, under the lock): what SoundTouch holds is dropped, and the file goes on from there.</summary>
        public void Restart()
        {
            Processor.Clear();
            ended = false;
        }

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
