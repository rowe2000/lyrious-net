using System;
using Avalonia;
using Lyrious.UILib;

namespace Lyrious.Desktop;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <param name="args">Optional: [0] Lyrious API base address (default http://localhost:7000).</param>
    [STAThread]
    public static void Main(string[] args)
    {
        using var authService = new ApiAuthService(args.Length > 0 ? args[0] : "http://localhost:7000");
        App.AuthService = authService;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}