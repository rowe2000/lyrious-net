using Avalonia;
using Avalonia.iOS;
using Foundation;
using Lyrious.UILib;

namespace Lyrious.iOS;

/// <summary>
/// Hosts the Lyrious.UILib app on iOS: launches it and receives application events from iOS.
/// All UI lives in Lyrious.UILib.
/// </summary>
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
