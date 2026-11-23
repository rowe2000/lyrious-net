using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia.Android;

namespace Lyrious.Android;

[Activity(
    Label = "Lyrious",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
// Opened by lyrious:// links, e.g. from the web app when this app is installed.
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "lyrious")]
public class MainActivity : AvaloniaMainActivity
{
}
