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
    /// https://lyrious.se (<see cref="SettingKeys.DefaultServer"/>).
    /// </param>
    [STAThread]
    public static void Main(string[] args)
    {
        var settings = new FileSettingsStore();
        App.Settings = settings;
        var host = args.Length > 0 ? args[0] : settings.Get(SettingKeys.Host) ?? SettingKeys.DefaultServer;
        // The offline cache and offline logins, in their own file (it's rewritten on every change).
        var offline = new FileSettingsStore("offline.json");
        var auth = new HttpAuthService(host, offline);
        App.AuthService = auth;
        App.SetlistService = new HttpSetlistService(auth, offline);
        App.CredentialStore = DesktopCredentialStore.Create();
        App.SoundStore = new FileSoundStore();
        // The MIDI tags in songs go to every MIDI output of this computer.
        App.Midi = new DesktopMidiOutput();
        // Sharing on the local network (the Sync page), on again if it was left on. A failure to
        // start (e.g. the port is taken) shows on the Sync page, where it's tried again.
        var sharing = new LanSharing(() => auth.ServerAddress);
        App.LanSharing = sharing;
        if (LanSharingSettings.Enabled)
        {
            _ = sharing.StartAsync(LanSharingSettings.Port).ContinueWith(_ => { }, System.Threading.Tasks.TaskScheduler.Default);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}