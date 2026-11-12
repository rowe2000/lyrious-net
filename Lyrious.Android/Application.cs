using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Lyrious.UILib;

namespace Lyrious.Android;

/// <summary>Hosts the Lyrious.UILib app on Android. All UI lives in Lyrious.UILib.</summary>
[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
