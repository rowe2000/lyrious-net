using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Lyrious.UILib;
using Lyrious.UILib.Localization;

namespace Lyrious.Desktop;

/// <summary>
/// Records from the Mac's sound input (System Settings › Sound › Input) through Core Audio's audio
/// queue (AudioToolbox), asked for in 16-bit stereo at 48 kHz; Core Audio converts the input to it
/// (a one-channel microphone comes in both channels). The first time, macOS asks whether Lyrious may
/// use the microphone (the app's Info.plist says why, see installers/macos/build-dmg.sh).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAudioRecorder : IAudioRecorder
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const int SampleRate = 48000;
    private const int Channels = 2;
    // A tenth of a second per buffer, three of them taking turns.
    private const int BufferBytes = SampleRate / 10 * Channels * 2;
    private const int BufferCount = 3;

    private IntPtr queue;
    private GCHandle self;
    private volatile bool running;
    private Action<short[], int>? handOver;
    private short[] samples = new short[BufferBytes / 2];

    // Kept alive while recording: Core Audio calls it from its own thread.
    private readonly InputCallback callback;

    public MacAudioRecorder()
    {
        callback = OnInput;
    }

    /// <summary>The Mac's sound devices that have inputs, by their names (System Settings › Sound › Input).</summary>
    public Task<IReadOnlyList<AudioInput>> GetInputsAsync()
    {
        var inputs = new List<AudioInput>();
        var devices = new Address(DevicesSelector, GlobalScope);
        if (AudioObjectGetPropertyDataSize(SystemObject, ref devices, 0, IntPtr.Zero, out var size) != 0 || size == 0)
        {
            return Task.FromResult<IReadOnlyList<AudioInput>>(inputs);
        }

        var ids = new uint[size / sizeof(uint)];
        var handle = GCHandle.Alloc(ids, GCHandleType.Pinned);
        try
        {
            if (AudioObjectGetPropertyData(SystemObject, ref devices, 0, IntPtr.Zero, ref size, handle.AddrOfPinnedObject()) != 0)
            {
                return Task.FromResult<IReadOnlyList<AudioInput>>(inputs);
            }
        }
        finally
        {
            handle.Free();
        }

        foreach (var device in ids)
        {
            if (InputChannels(device) > 0 && StringProperty(device, DeviceUidSelector) is { } uid)
            {
                inputs.Add(new AudioInput(uid, StringProperty(device, NameSelector) ?? uid));
            }
        }

        return Task.FromResult<IReadOnlyList<AudioInput>>(inputs);
    }

    public Task<int> StartAsync(string? inputId, Action<short[], int> samples)
    {
        handOver = samples;
        var format = new AudioStreamBasicDescription
        {
            SampleRate = SampleRate,
            FormatId = 0x6C70636D, // 'lpcm'
            FormatFlags = 0x4 | 0x8, // signed integers, packed
            BytesPerPacket = Channels * 2,
            FramesPerPacket = 1,
            BytesPerFrame = Channels * 2,
            ChannelsPerFrame = Channels,
            BitsPerChannel = 16,
        };

        self = GCHandle.Alloc(this);
        // No run loop: Core Audio calls back on a thread of its own.
        var status = AudioQueueNewInput(ref format, Marshal.GetFunctionPointerForDelegate(callback), GCHandle.ToIntPtr(self), IntPtr.Zero, IntPtr.Zero, 0, out queue);
        if (status != 0)
        {
            self.Free();
            throw new InvalidOperationException(L.T("This Mac's sound input couldn't be used (Core Audio error {0}).", status));
        }

        // The input chosen in Settings, by its id; not connected (or refused), the Mac's default input is used.
        if (!string.IsNullOrEmpty(inputId))
        {
            var uid = CFStringCreateWithCharacters(IntPtr.Zero, inputId, inputId.Length);
            if (uid != IntPtr.Zero)
            {
                AudioQueueSetProperty(queue, CurrentDeviceProperty, ref uid, (uint)IntPtr.Size);
                CFRelease(uid);
            }
        }

        for (var i = 0; i < BufferCount; i++)
        {
            if (AudioQueueAllocateBuffer(queue, BufferBytes, out var buffer) == 0)
            {
                AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
            }
        }

        running = true;
        status = AudioQueueStart(queue, IntPtr.Zero);
        if (status != 0)
        {
            Dispose();
            // Refused by macOS: the microphone isn't allowed for Lyrious.
            throw new InvalidOperationException(L.T("Recording isn't allowed. Allow Lyrious to use the microphone in System Settings › Privacy & Security › Microphone (Core Audio error {0}).", status));
        }

        return Task.FromResult(SampleRate);
    }

    public Task StopAsync()
    {
        if (queue != IntPtr.Zero)
        {
            running = false;
            AudioQueueStop(queue, 1);
            Dispose();
        }

        return Task.CompletedTask;
    }

    private void Dispose()
    {
        running = false;
        if (queue != IntPtr.Zero)
        {
            AudioQueueDispose(queue, 1);
            queue = IntPtr.Zero;
        }

        if (self.IsAllocated)
        {
            self.Free();
        }
    }

    /// <summary>A buffer filled by Core Audio (its thread): handed on, then given back to be filled again.</summary>
    private static void OnInput(IntPtr userData, IntPtr queue, IntPtr buffer, IntPtr startTime, uint packets, IntPtr packetDescriptions)
    {
        if (GCHandle.FromIntPtr(userData).Target is not MacAudioRecorder recorder)
        {
            return;
        }

        // An AudioQueueBuffer: its capacity (4 bytes, then 4 of padding), the data's address, its size.
        var data = Marshal.ReadIntPtr(buffer, 8);
        var bytes = Marshal.ReadInt32(buffer, 16);
        var count = Math.Min(bytes / 2, recorder.samples.Length);
        if (count > 0 && data != IntPtr.Zero)
        {
            Marshal.Copy(data, recorder.samples, 0, count);
            recorder.handOver?.Invoke(recorder.samples, count);
        }

        if (recorder.running)
        {
            AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
        }
    }

    /// <summary>How many input channels the device has (0 for one that only plays).</summary>
    private static int InputChannels(uint device)
    {
        var configuration = new Address(StreamConfigurationSelector, InputScope);
        if (AudioObjectGetPropertyDataSize(device, ref configuration, 0, IntPtr.Zero, out var size) != 0 || size == 0)
        {
            return 0;
        }

        var list = Marshal.AllocHGlobal((int)size);
        try
        {
            if (AudioObjectGetPropertyData(device, ref configuration, 0, IntPtr.Zero, ref size, list) != 0)
            {
                return 0;
            }

            // An AudioBufferList: the number of buffers (4 bytes, then 4 of padding), then each buffer's
            // channels (4), byte size (4) and data address (8).
            var buffers = Marshal.ReadInt32(list);
            var channels = 0;
            for (var i = 0; i < buffers; i++)
            {
                channels += Marshal.ReadInt32(list, 8 + i * 16);
            }

            return channels;
        }
        finally
        {
            Marshal.FreeHGlobal(list);
        }
    }

    /// <summary>A device's text property (its name or id); null when it hasn't one.</summary>
    private static string? StringProperty(uint device, uint selector)
    {
        var address = new Address(selector, GlobalScope);
        var size = (uint)IntPtr.Size;
        var value = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, value) != 0)
            {
                return null;
            }

            var text = Marshal.ReadIntPtr(value);
            if (text == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var length = (int)CFStringGetLength(text);
                var characters = new char[length];
                CFStringGetCharacters(text, new CFRange { Location = 0, Length = length }, characters);
                return new string(characters);
            }
            finally
            {
                CFRelease(text);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(value);
        }
    }

    // Core Audio's four-letter codes.
    private const uint SystemObject = 1;
    private const uint DevicesSelector = 0x64657623; // 'dev#'
    private const uint NameSelector = 0x6C6E616D; // 'lnam'
    private const uint DeviceUidSelector = 0x75696420; // 'uid '
    private const uint StreamConfigurationSelector = 0x736C6179; // 'slay'
    private const uint GlobalScope = 0x676C6F62; // 'glob'
    private const uint InputScope = 0x696E7074; // 'inpt'
    private const uint CurrentDeviceProperty = 0x61716364; // 'aqcd'

    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)]
    private struct Address(uint selector, uint scope)
    {
        public uint Selector = selector;
        public uint Scope = scope;
        public uint Element = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyDataSize(uint objectId, ref Address address, uint qualifierSize, IntPtr qualifier, out uint size);

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyData(uint objectId, ref Address address, uint qualifierSize, IntPtr qualifier, ref uint size, IntPtr data);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueSetProperty(IntPtr queue, uint property, ref IntPtr data, uint size);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, string characters, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr text);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern void CFStringGetCharacters(IntPtr text, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InputCallback(IntPtr userData, IntPtr queue, IntPtr buffer, IntPtr startTime, uint packets, IntPtr packetDescriptions);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueNewInput(ref AudioStreamBasicDescription format, IntPtr callback, IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint size, out IntPtr buffer);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint packetDescriptions, IntPtr descriptions);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStop(IntPtr queue, byte immediate);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueDispose(IntPtr queue, byte immediate);
}
