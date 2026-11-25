using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Lyrious.UILib;
using Lyrious.UILib.Localization;

namespace Lyrious.Webapp;

/// <summary>
/// Records from the browser's sound input (<c>lyriousRecorder</c> in wwwroot/main.js): the browser
/// asks the member the first time, and gives two channels where the input has them. What's recorded
/// is taken from the page a few times a second and handed on.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserAudioRecorder : IAudioRecorder
{
    private static readonly TimeSpan TakeEvery = TimeSpan.FromMilliseconds(250);

    private Action<short[], int>? handOver;
    private Task? taking;
    private bool running;

    /// <summary>Whether the browser can record (a sound input and audio worklets).</summary>
    public static bool IsAvailable()
    {
        try
        {
            return JsIsSupported();
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>The browser's sound inputs (named once it's been allowed to use the microphone).</summary>
    public async Task<IReadOnlyList<AudioInput>> GetInputsAsync()
    {
        try
        {
            using var json = JsonDocument.Parse(await JsInputs());
            return json.RootElement.EnumerateArray()
                .Select(input => new AudioInput(input.GetProperty("id").GetString() ?? "", input.GetProperty("name").GetString() ?? ""))
                .Where(input => input.Id.Length > 0)
                .ToList();
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            return [];
        }
    }

    public async Task<int> StartAsync(string? inputId, Action<short[], int> samples)
    {
        handOver = samples;
        double sampleRate;
        try
        {
            sampleRate = await JsStart(inputId ?? "");
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException(L.T("The browser couldn't record: {0}.", ex.Message), ex);
        }

        running = true;
        taking = TakeAsync();
        return (int)sampleRate;
    }

    public async Task StopAsync()
    {
        running = false;
        if (taking != null)
        {
            await taking;
        }

        // The last of it, then the microphone is let go.
        Take();
        JsStop();
    }

    /// <summary>Takes what was recorded a few times a second, while recording.</summary>
    private async Task TakeAsync()
    {
        while (running)
        {
            await Task.Delay(TakeEvery);
            Take();
        }
    }

    private void Take()
    {
        var base64 = JsTake();
        if (string.IsNullOrEmpty(base64) || handOver == null)
        {
            return;
        }

        var bytes = Convert.FromBase64String(base64);
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
        handOver(samples, samples.Length);
    }

    [JSImport("globalThis.lyriousRecorder.isSupported")]
    private static partial bool JsIsSupported();

    [JSImport("globalThis.lyriousRecorder.inputs")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> JsInputs();

    [JSImport("globalThis.lyriousRecorder.start")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    private static partial Task<double> JsStart(string deviceId);

    [JSImport("globalThis.lyriousRecorder.take")]
    private static partial string JsTake();

    [JSImport("globalThis.lyriousRecorder.stop")]
    private static partial void JsStop();
}
