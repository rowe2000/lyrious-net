using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lyrious.UILib;

namespace Lyrious.Web;

/// <summary>
/// Sends MIDI on every MIDI output the browser offers, through the Web MIDI API (<c>lyriousMidi</c>
/// in wwwroot/main.js): Chromium browsers and Firefox, which ask the user the first time; Safari
/// has no Web MIDI, so nothing is sent there.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserMidiOutput : IMidiOutput
{
    public void Send(IReadOnlyList<byte[]> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            // "C0 14;CF 1": messages split by ';', their bytes (hex) by spaces.
            SendMessages(string.Join(';', messages.Select(message => string.Join(' ', message.Select(b => b.ToString("X2"))))));
        }
        catch (JSException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't send MIDI: {ex.Message}");
        }
    }

    [JSImport("globalThis.lyriousMidi.send")]
    private static partial void SendMessages(string messages);
}
