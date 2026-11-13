using System;
using Avalonia;
using Lyrious.Client;
using Lyrious.UILib;

namespace Lyrious.Desktop;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <param name="args">
    /// Optional: [0] Lyrious API base address. Else the host chosen on the Sync page last time, else
    /// http://localhost:7000.
    /// </param>
    [STAThread]
    public static void Main(string[] args)
    {
        var settings = new FileSettingsStore();
        App.Settings = settings;
        var host = args.Length > 0 ? args[0] : settings.Get(SettingKeys.Host) ?? "http://localhost:7000";
        var auth = new HttpAuthService(host);
        App.AuthService = auth;
        App.SetlistService = new HttpSetlistService(auth);
        App.CredentialStore = DesktopCredentialStore.Create();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}