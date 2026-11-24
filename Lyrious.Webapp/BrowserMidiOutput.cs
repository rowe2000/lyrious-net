using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lyrious.UILib;

namespace Lyrious.Webapp;

/// <summary>
/// Sends MIDI on every MIDI output the browser offers, through the Web MIDI API (<c>lyriousMidi</c>
/// in wwwroot/main.js): Chromium browsers and Firefox, which ask the user the first time; Safari
/// has no Web MIDI, so nothing is sent there.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserMidiOutput : IMidiOutput, IMidiRequester
{
    /// <summary>Sends the request on every output and gathers the System Exclusive answers on the inputs (see <c>lyriousMidi.request</c>).</summary>
    public async System.Threading.Tasks.Task<IReadOnlyList<byte[]>> RequestAsync(byte[] request, int lastIndex, byte lastValue, System.TimeSpan timeout)
    {
        try
        {
            // Messages as hex, split by ';' (as sent).
            var replies = await Request(string.Join(' ', request.Select(b => b.ToString("X2"))), lastIndex, lastValue, (int)timeout.TotalMilliseconds);
            return replies.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(message => message.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Select(b => System.Convert.ToByte(b, 16)).ToArray())
                .ToList();
        }
        catch (JSException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't ask by MIDI: {ex.Message}");
            return [];
        }
    }

    [JSImport("globalThis.lyriousMidi.request")]
    private static partial System.Threading.Tasks.Task<string> Request(string message, int lastIndex, int lastValue, int timeoutMs);

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
