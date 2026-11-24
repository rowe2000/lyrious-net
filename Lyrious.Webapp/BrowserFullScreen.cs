using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lyrious.UILib;

namespace Lyrious.Webapp;

/// <summary>
/// Shows the app full screen in the browser (the Fullscreen API, through <c>lyriousFullScreen</c> in
/// wwwroot/main.js). The browser only allows it in answer to a tap or a key: asked for otherwise (e.g.
/// the setting was on when the app started), it starts at the next one. Leaving it with Escape (or the
/// browser's own way) raises <see cref="Exited"/>.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserFullScreen : IFullScreenHost
{
    public BrowserFullScreen()
    {
        try
        {
            IsSupported = JsIsSupported();
            if (IsSupported)
            {
                JsOnExited(() => Exited?.Invoke());
            }
        }
        catch (JSException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Full screen isn't available: {ex.Message}");
        }
    }

    public bool IsSupported { get; }

    public event Action? Exited;

    public void Set(bool on)
    {
        try
        {
            JsSet(on);
        }
        catch (JSException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't change full screen: {ex.Message}");
        }
    }

    [JSImport("globalThis.lyriousFullScreen.isSupported")]
    private static partial bool JsIsSupported();

    [JSImport("globalThis.lyriousFullScreen.set")]
    private static partial void JsSet(bool on);

    [JSImport("globalThis.lyriousFullScreen.onExited")]
    private static partial void JsOnExited([JSMarshalAs<JSType.Function>] Action callback);
}
