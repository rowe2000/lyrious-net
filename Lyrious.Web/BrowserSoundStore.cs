using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Lyrious.UILib;

namespace Lyrious.Web;

/// <summary>
/// Sound files for songs' media links in the browser's own storage (IndexedDB, functions in
/// wwwroot/main.js), played in the page. Kept per address (like the rest of the web app's data on
/// the device).
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserSoundStore : ISoundStore
{
    public BrowserSoundStore() => SetEndedCallback(() => PlayingChanged?.Invoke());

    public string? Playing => GetPlaying();

    public event Action? PlayingChanged;

    private double speed = 1;
    private double pitch;

    // The browser keeps the pitch when the speed changes (playbackRate with preservesPitch).
    public bool CanChangeSpeed => true;

    // Needs an AudioWorklet: only in a secure context (https, or http://localhost).
    public bool CanChangePitch => GetCanChangePitch();

    public double Speed
    {
        get => speed;
        set
        {
            speed = Math.Clamp(value, 0.25, 4);
            SetSpeedNow(speed);
        }
    }

    public double Pitch
    {
        get => pitch;
        set
        {
            pitch = Math.Clamp(value, -24, 24);
            SetPitchNow(pitch);
        }
    }

    public async Task SaveAsync(string id, Stream content)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        if (copy.Length > Lyrious.UILib.Setlists.MediaLinks.MaxSoundSize)
        {
            throw new InvalidOperationException("The file is too big.");
        }

        await SaveNow(id, new ArraySegment<byte>(copy.GetBuffer(), 0, (int)copy.Length), MimeTypeOf(id));
    }

    public async Task<bool> PlayAsync(string id)
    {
        var played = await PlayNow(id);
        PlayingChanged?.Invoke();
        return played;
    }

    public void Stop()
    {
        StopNow();
        PlayingChanged?.Invoke();
    }

    private static string MimeTypeOf(string id) => Path.GetExtension(id).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" or ".aac" => "audio/mp4",
        ".wav" => "audio/wav",
        ".ogg" or ".oga" => "audio/ogg",
        ".opus" => "audio/ogg; codecs=opus",
        ".flac" => "audio/flac",
        ".webm" => "audio/webm",
        _ => "application/octet-stream",
    };

    [JSImport("globalThis.lyriousSounds.save")]
    private static partial Task SaveNow(string id, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> bytes, string type);

    [JSImport("globalThis.lyriousSounds.play")]
    private static partial Task<bool> PlayNow(string id);

    [JSImport("globalThis.lyriousSounds.stop")]
    private static partial void StopNow();

    [JSImport("globalThis.lyriousSounds.playing")]
    private static partial string? GetPlaying();

    [JSImport("globalThis.lyriousSounds.canChangePitch")]
    private static partial bool GetCanChangePitch();

    [JSImport("globalThis.lyriousSounds.setSpeed")]
    private static partial void SetSpeedNow(double speed);

    [JSImport("globalThis.lyriousSounds.setPitch")]
    private static partial void SetPitchNow(double semitones);

    [JSImport("globalThis.lyriousSounds.onChanged")]
    private static partial void SetEndedCallback([JSMarshalAs<JSType.Function>] Action changed);
}
