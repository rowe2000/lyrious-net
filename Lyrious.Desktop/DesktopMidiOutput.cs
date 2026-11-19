using System;
using System.Collections.Generic;
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
internal sealed class DesktopMidiOutput : IMidiOutput
{
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

    /// <summary>A program change or control change message as an event; null for anything else.</summary>
    private static MidiEvent? ToEvent(byte[] message)
    {
        if (message.Length < 2)
        {
            return null;
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
