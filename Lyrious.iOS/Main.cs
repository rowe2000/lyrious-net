using Lyrious.Client;
using Lyrious.UILib;
using UIKit;

namespace Lyrious.iOS;

public class Application
{
    // The main entry point of the application.
    static void Main(string[] args)
    {
        // Connects the app to the Lyrious API: the host chosen on the Sync page last time, else
        // http://localhost:5180 (the simulator's own computer; plain http is only allowed to local
        // addresses, see Info.plist). Settings and the offline cache are kept in the app's folder.
        var settings = new FileSettingsStore();
        App.Settings = settings;
        var host = settings.Get(SettingKeys.Host) ?? "http://localhost:5180";
        var offline = new FileSettingsStore("offline.json");
        var auth = new HttpAuthService(host, offline);
        App.AuthService = auth;
        App.SetlistService = new HttpSetlistService(auth, offline);

        // To use a different application delegate class than AppDelegate, specify it here.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
