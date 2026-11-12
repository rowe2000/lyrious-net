using Avalonia;
using Lyrious.UILib.Login;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Lyrious.UILib
{
	public class App : Application
	{
		/// <summary>
		/// How the login page checks credentials. Hosts set this before starting the app;
		/// until then login explains that no server is configured.
		/// </summary>
		public static IAuthService AuthService { get; set; } = new UnconfiguredAuthService();

		public override void Initialize()
		{
			// Default follows the system theme; ThemeVariant.Dark or ThemeVariant.Light force one.
			RequestedThemeVariant = ThemeVariant.Default;
			Styles.Add(new FluentTheme());
			LyriousTheme.AddTo(Resources);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.MainWindow = new MainWindow();
			}
			else if (ApplicationLifetime is IActivityApplicationLifetime activity)
			{
				// Hosts that create their view on demand (Avalonia 12 browser/mobile).
				activity.MainViewFactory = () => new MainView();
			}
			else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
			{
				// Window-less hosts such as the browser.
				singleView.MainView = new MainView();
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
