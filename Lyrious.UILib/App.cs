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

		/// <summary>
		/// Settings kept on this device (e.g. the API host). Hosts set this before starting the app;
		/// by default settings last only while the app runs.
		/// </summary>
		public static ISettingsStore Settings { get; set; } = new MemorySettingsStore();

		/// <summary>
		/// Sharing on the local network (the Sync page). Hosts that can listen on the
		/// network set this before starting the app; by default it isn't offered.
		/// </summary>
		public static ILanSharing? LanSharing { get; set; }

		/// <summary>
		/// Where sound files for songs' media links are kept on this device. Hosts set this before
		/// starting the app; by default sound files aren't offered.
		/// </summary>
		public static ISoundStore? SoundStore { get; set; }

		/// <summary>
		/// Sends the MIDI tags in songs' lyrics (see <see cref="Midi"/>) on every MIDI output of this
		/// device. Hosts set this before starting the app; by default no MIDI is sent.
		/// </summary>
		public static IMidiOutput? Midi { get; set; }

		public override void Initialize()
		{
			// Default follows the system theme; ThemeVariant.Dark or ThemeVariant.Light force one.
			RequestedThemeVariant = ThemeVariant.Default;
			Styles.Add(new FluentTheme());
			LyriousTheme.AddTo(Resources);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			// The user's own colours (Settings → Colors), over the theme's.
			ColorSettings.Apply();
			EscapeKey.Register();
			UiScale.RegisterPopups();
			StartMainView();
		}

		private void StartMainView()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.MainWindow = new MainWindow();
			}
			else if (ApplicationLifetime is IActivityApplicationLifetime activity)
			{
				// Hosts that create their view on demand (Avalonia 12 browser/mobile).
				activity.MainViewFactory = () => UiScale.Wrap(new MainView());
			}
			else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
			{
				// Window-less hosts such as the browser.
				singleView.MainView = UiScale.Wrap(new MainView());
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
