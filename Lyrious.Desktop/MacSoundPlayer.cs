using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Lyrious.Desktop;

/// <summary>
/// Plays a sound file on macOS in the app, through AVFoundation's audio engine: the file's player,
/// then a time-pitch unit (which changes the speed and the pitch each without the other), then the
/// output. Reached through the Objective-C runtime, so it needs nothing but macOS itself. Tells when
/// the file has played to its end (<see cref="Ended"/>, on a background thread).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacSoundPlayer : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    private readonly IntPtr engine;
    private readonly IntPtr player;
    private readonly IntPtr timePitch;
    private readonly IntPtr file;
    private readonly long length;
    private readonly double sampleRate;
    // Where the player was last scheduled from (a seek): its own time counts from there.
    private long startFrame;
    private readonly Timer endWatch;
    // The native objects are used and released under this, from any thread.
    private readonly object gate = new();
    private bool released;

    /// <summary>Whether AVFoundation's audio engine is there (it is on any macOS the app runs on).</summary>
    public static bool IsAvailable { get; } = Load();

    /// <summary>Raised once, when the file has played to its end (not when stopped).</summary>
    public event Action? Ended;

    private MacSoundPlayer(IntPtr engine, IntPtr player, IntPtr timePitch, IntPtr file, long length, double sampleRate)
    {
        this.sampleRate = sampleRate > 0 ? sampleRate : 44100;
        this.engine = engine;
        this.player = player;
        this.timePitch = timePitch;
        this.file = file;
        this.length = length;
        endWatch = new Timer(_ => CheckEnded(), null, 250, 250);
    }

    /// <summary>Starts playing the file at the speed (1 = as recorded) and pitch (semitones); throws if it can't.</summary>
    public static MacSoundPlayer Play(string path, double speed, double pitch)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("AVFoundation isn't available.");
        }

        var pool = objc_autoreleasePoolPush();
        IntPtr engine = 0, player = 0, timePitch = 0, file = 0;
        try
        {
            engine = New("AVAudioEngine");
            player = New("AVAudioPlayerNode");
            timePitch = New("AVAudioUnitTimePitch");

            var url = Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(path));
            file = SendWithError(Send(Class("AVAudioFile"), Sel("alloc")), Sel("initForReading:error:"), url, out var error);
            if (file == 0)
            {
                throw new InvalidOperationException($"macOS can't read the file. {Describe(error)}");
            }

            var format = Send(file, Sel("processingFormat"));
            var length = SendLong(file, Sel("length"));

            Send(engine, Sel("attachNode:"), player);
            Send(engine, Sel("attachNode:"), timePitch);
            Send(engine, Sel("connect:to:format:"), player, timePitch, format);
            Send(engine, Sel("connect:to:format:"), timePitch, Send(engine, Sel("mainMixerNode")), format);
            Send(player, Sel("scheduleFile:atTime:completionHandler:"), file, 0, 0);
            Send(engine, Sel("prepare"));
            if (!SendBoolWithError(engine, Sel("startAndReturnError:"), out error))
            {
                throw new InvalidOperationException($"The sound couldn't start. {Describe(error)}");
            }

            var sound = new MacSoundPlayer(engine, player, timePitch, file, length, SendDouble(format, Sel("sampleRate")));
            sound.SetSpeed(speed);
            sound.SetPitch(pitch);
            Send(player, Sel("play"));
            return sound;
        }
        catch
        {
            foreach (var created in new[] { file, timePitch, player, engine })
            {
                if (created != 0)
                {
                    Send(created, Sel("release"));
                }
            }

            throw;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>The speed, 1 being as recorded (the pitch stays).</summary>
    public void SetSpeed(double speed)
    {
        lock (gate)
        {
            if (!released)
            {
                SendFloat(timePitch, Sel("setRate:"), (float)Math.Clamp(speed, 1.0 / 32, 32));
            }
        }
    }

    /// <summary>The pitch in semitones, 0 being as recorded (the speed stays).</summary>
    public void SetPitch(double semitones)
    {
        lock (gate)
        {
            if (!released)
            {
                SendFloat(timePitch, Sel("setPitch:"), (float)Math.Clamp(semitones * 100, -2400, 2400));
            }
        }
    }

    /// <summary>How long the file is.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(length / sampleRate);

    /// <summary>Where in the file it plays now (in the recording's time); null once it's let go of.</summary>
    public TimeSpan? Position
    {
        get
        {
            lock (gate)
            {
                if (released)
                {
                    return null;
                }

                var pool = objc_autoreleasePoolPush();
                try
                {
                    return TimeSpan.FromSeconds(Math.Min(PlayedFrame(), length) / sampleRate);
                }
                finally
                {
                    objc_autoreleasePoolPop(pool);
                }
            }
        }
    }

    /// <summary>Plays on from <paramref name="position"/> in the file.</summary>
    public void Seek(TimeSpan position)
    {
        lock (gate)
        {
            if (released || length <= 0)
            {
                return;
            }

            var frame = Math.Clamp((long)(position.TotalSeconds * sampleRate), 0, length - 1);
            var pool = objc_autoreleasePoolPush();
            try
            {
                // Stopping empties what's scheduled and sets the player's own time back to 0.
                Send(player, Sel("stop"));
                startFrame = frame;
                SendSchedule(player, Sel("scheduleSegment:startingFrame:frameCount:atTime:completionHandler:"), file, frame, (uint)Math.Min(length - frame, uint.MaxValue), 0, 0);
                Send(player, Sel("play"));
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }
    }

    /// <summary>The frame of the file played now: where it was scheduled from, plus the player's own time (under the lock, in a pool).</summary>
    private long PlayedFrame()
    {
        var nodeTime = Send(player, Sel("lastRenderTime"));
        var playerTime = nodeTime == 0 ? 0 : Send(player, Sel("playerTimeForNodeTime:"), nodeTime);
        return startFrame + (playerTime == 0 ? 0 : Math.Max(0, SendLong(playerTime, Sel("sampleTime"))));
    }

    /// <summary>The output's volume, 0 to 1.</summary>
    public void SetVolume(double volume)
    {
        lock (gate)
        {
            if (!released)
            {
                SendFloat(Send(engine, Sel("mainMixerNode")), Sel("setOutputVolume:"), (float)Math.Clamp(volume, 0, 1));
            }
        }
    }

    /// <summary>Stops the sound and lets go of it (without <see cref="Ended"/>).</summary>
    public void Dispose() => Release();

    /// <summary>Stops and lets go of everything, once; true for the call that did.</summary>
    private bool Release()
    {
        lock (gate)
        {
            if (released)
            {
                return false;
            }

            released = true;
            endWatch.Dispose();
            var pool = objc_autoreleasePoolPush();
            try
            {
                Send(player, Sel("stop"));
                Send(engine, Sel("stop"));
                Send(file, Sel("release"));
                Send(timePitch, Sel("release"));
                Send(player, Sel("release"));
                Send(engine, Sel("release"));
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }

            return true;
        }
    }

    /// <summary>Whether the player has read the whole file (the samples it has played, before the time-pitch unit).</summary>
    private void CheckEnded()
    {
        bool ended;
        lock (gate)
        {
            if (released)
            {
                return;
            }

            var pool = objc_autoreleasePoolPush();
            try
            {
                ended = PlayedFrame() >= length;
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        // Let go first (so a new sound can start from Ended), then tell; not if it was stopped meanwhile.
        if (ended && Release())
        {
            Ended?.Invoke();
        }
    }

    /// <summary>
    /// How loud the file is over time, read through AVFoundation (its samples as floats, a channel at a
    /// time); null when macOS can't read it.
    /// </summary>
    public static Lyrious.UILib.SoundWave? ReadWave(string path)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var pool = objc_autoreleasePoolPush();
        IntPtr file = 0, buffer = 0;
        try
        {
            var url = Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(path));
            file = SendWithError(Send(Class("AVAudioFile"), Sel("alloc")), Sel("initForReading:error:"), url, out _);
            if (file == 0)
            {
                return null;
            }

            var format = Send(file, Sel("processingFormat"));
            var channels = (int)SendUInt(format, Sel("channelCount"));
            var builder = new Lyrious.UILib.SoundWaveBuilder(SendDouble(format, Sel("sampleRate")), channels);
            const uint Frames = 65536;
            buffer = SendInitBuffer(Send(Class("AVAudioPCMBuffer"), Sel("alloc")), Sel("initWithPCMFormat:frameCapacity:"), format, Frames);
            if (buffer == 0 || channels < 1)
            {
                return null;
            }

            var pointers = new IntPtr[channels];
            var samples = new float[channels][];
            while (SendRead(file, Sel("readIntoBuffer:frameCount:error:"), buffer, Frames, out _))
            {
                var count = (int)SendUInt(buffer, Sel("frameLength"));
                var data = Send(buffer, Sel("floatChannelData"));
                if (count == 0 || data == 0)
                {
                    break;
                }

                Marshal.Copy(data, pointers, 0, channels);
                for (var c = 0; c < channels; c++)
                {
                    samples[c] ??= new float[Frames];
                    Marshal.Copy(pointers[c], samples[c], 0, count);
                }

                // Interleaved, as the builder takes them.
                for (var i = 0; i < count; i++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        builder.Add(samples[c][i]);
                    }
                }
            }

            return builder.Count > 0 ? builder.Build() : null;
        }
        finally
        {
            foreach (var created in new[] { buffer, file })
            {
                if (created != 0)
                {
                    Send(created, Sel("release"));
                }
            }

            objc_autoreleasePoolPop(pool);
        }
    }

    private static bool Load()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
            return objc_getClass("AVAudioEngine") != 0 && objc_getClass("AVAudioUnitTimePitch") != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static IntPtr New(string className) => Send(Send(Class(className), Sel("alloc")), Sel("init"));

    private static IntPtr Class(string name) => objc_getClass(name) is var c && c != 0 ? c : throw new InvalidOperationException($"{name} isn't available.");

    private static IntPtr Sel(string name) => sel_registerName(name);

    /// <summary>An (autoreleased) NSString.</summary>
    private static IntPtr NSString(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>An NSError's description.</summary>
    private static string Describe(IntPtr error)
    {
        if (error == 0)
        {
            return "";
        }

        var text = Send(Send(error, Sel("localizedDescription")), Sel("UTF8String"));
        return text == 0 ? "" : Marshal.PtrToStringUTF8(text) ?? "";
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendWithError(IntPtr receiver, IntPtr selector, IntPtr a, out IntPtr error);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolWithError(IntPtr receiver, IntPtr selector, out IntPtr error);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern long SendLong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendFloat(IntPtr receiver, IntPtr selector, float value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern uint SendUInt(IntPtr receiver, IntPtr selector);

    // initWithPCMFormat:frameCapacity:
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendInitBuffer(IntPtr receiver, IntPtr selector, IntPtr format, uint frameCapacity);

    // readIntoBuffer:frameCount:error:
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendRead(IntPtr receiver, IntPtr selector, IntPtr buffer, uint frameCount, out IntPtr error);

    // scheduleSegment:startingFrame:frameCount:atTime:completionHandler:
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendSchedule(IntPtr receiver, IntPtr selector, IntPtr file, long startingFrame, uint frameCount, IntPtr when, IntPtr completionHandler);
}
