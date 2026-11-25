using System;
using System.Linq;
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
    /// https://www.lyrious.se (<see cref="SettingKeys.DefaultServer"/>). A lyrious:// link (the web app
    /// opening the installed app, see the installers) only starts the app.
    /// </param>
    [STAThread]
    public static void Main(string[] args)
    {
        var settings = new FileSettingsStore();
        App.Settings = settings;
        args = args.Where(arg => !arg.StartsWith("lyrious:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var host = args.Length > 0 ? args[0] : settings.Get(SettingKeys.Host) ?? SettingKeys.DefaultServer;
        // The offline cache and offline logins, in their own file (it's rewritten on every change).
        var offline = new FileSettingsStore("offline.json");
        var auth = new HttpAuthService(host, offline);
        App.AuthService = auth;
        App.SetlistService = new HttpSetlistService(auth, offline);
        App.CredentialStore = DesktopCredentialStore.Create();
        App.SoundStore = new FileSoundStore();
        // Recording the band as it plays (a device setting): Windows through WASAPI, the Mac through Core Audio.
        if (OperatingSystem.IsWindows())
        {
            App.AudioRecorder = new WindowsAudioRecorder();
        }
        else if (OperatingSystem.IsMacOS())
        {
            App.AudioRecorder = new MacAudioRecorder();
        }
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