using Avalonia;
using Lyrious.UILib.Login;
using Lyrious.UILib.Setlists;
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

		/// <summary>
		/// Where the setlist and lyrics pages get their data. Hosts set this before starting the app;
		/// until then those pages explain that no server is configured.
		/// </summary>
		public static ISetlistService SetlistService { get; set; } = new UnconfiguredSetlistService();

		/// <summary>
		/// Where a password can be saved after login (and filled in from next time). Hosts set this
		/// before starting the app; by default nothing is offered.
		/// </summary>
		public static ICredentialStore CredentialStore { get; set; } = new NoCredentialStore();

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
