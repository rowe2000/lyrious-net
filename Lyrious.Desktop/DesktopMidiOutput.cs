using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lyrious.UILib;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Lyrious.Desktop;

/// <summary>
/// Sends MIDI on every MIDI output of this computer (Windows and macOS, through DryWetMIDI). The
/// outputs are looked up at each send, so one plugged in since is used too. Sent in the background,
/// one batch after the other in the order asked for; an output that fails is skipped.
/// </summary>
internal sealed class DesktopMidiOutput : IMidiOutput, IMidiRequester
{
    /// <summary>
    /// Listens on every MIDI input for System Exclusive messages, sends the request on every output,
    /// and gives back what came until the last of the answer (or the time ran out).
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> RequestAsync(byte[] request, int lastIndex, byte lastValue, TimeSpan timeout)
    {
        var received = new List<byte[]>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputs = new List<InputDevice>();

        void OnEvent(object? sender, MidiEventReceivedEventArgs e)
        {
            if (e.Event is not NormalSysExEvent sysEx)
            {
                return;
            }

            // Its bytes after F0 (with the F7): F0 first, as it was sent.
            var message = new byte[sysEx.Data.Length + 1];
            message[0] = 0xF0;
            sysEx.Data.CopyTo(message, 1);
            lock (received)
            {
                received.Add(message);
            }

            if (message.Length > lastIndex && message[lastIndex] == lastValue)
            {
                done.TrySetResult();
            }
        }

        try
        {
            foreach (var input in InputDevice.GetAll())
            {
                try
                {
                    input.EventReceived += OnEvent;
                    input.StartEventsListening();
                    inputs.Add(input);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Couldn't listen to {input.Name}: {ex.Message}");
                    input.EventReceived -= OnEvent;
                    input.Dispose();
                }
            }

            Send([request]);
            await Task.WhenAny(done.Task, Task.Delay(timeout));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MIDI isn't available: {ex.Message}");
        }
        finally
        {
            foreach (var input in inputs)
            {
                try
                {
                    input.StopEventsListening();
                }
                catch (Exception)
                {
                    // Unplugged meanwhile.
                }

                input.EventReceived -= OnEvent;
                input.Dispose();
            }
        }

        lock (received)
        {
            return received.ToList();
        }
    }

    private readonly object gate = new();

    // The batches waiting, each sent after the one before.
    private Task queue = Task.CompletedTask;

    public void Send(IReadOnlyList<byte[]> messages)
    {
        var events = new List<MidiEvent>();
        foreach (var message in messages)
        {
            if (ToEvent(message) is { } midiEvent)
            {
                events.Add(midiEvent);
            }
        }

        if (events.Count == 0)
        {
            return;
        }

        lock (gate)
        {
            queue = queue.ContinueWith(_ => SendNow(events), TaskScheduler.Default);
        }
    }

    private static void SendNow(IReadOnlyList<MidiEvent> events)
    {
        ICollection<OutputDevice> devices;
        try
        {
            devices = OutputDevice.GetAll();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MIDI isn't available: {ex.Message}");
            return;
        }

        foreach (var device in devices)
        {
            using (device)
            {
                try
                {
                    foreach (var midiEvent in events)
                    {
                        device.SendEvent(midiEvent);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Couldn't send MIDI to {device.Name}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>A program change, control change or System Exclusive message as an event; null for anything else.</summary>
    private static MidiEvent? ToEvent(byte[] message)
    {
        if (message.Length < 2)
        {
            return null;
        }

        // System Exclusive (e.g. a Yamaha parameter change): its bytes after F0, up to and with the F7.
        if (message[0] == 0xF0)
        {
            return message[^1] == 0xF7 ? new NormalSysExEvent(message[1..]) : null;
        }

        var channel = (FourBitNumber)(message[0] & 0x0F);
        return (message[0] & 0xF0) switch
        {
            0xC0 => new ProgramChangeEvent((SevenBitNumber)(message[1] & 0x7F)) { Channel = channel },
            0xB0 when message.Length >= 3 => new ControlChangeEvent((SevenBitNumber)(message[1] & 0x7F), (SevenBitNumber)(message[2] & 0x7F)) { Channel = channel },
            _ => null,
        };
    }
}
