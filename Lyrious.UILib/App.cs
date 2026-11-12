using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Lyrious.UILib
{
	public class App : Application
	{
		public override void Initialize()
		{
			// Default follows the system theme; ThemeVariant.Dark or ThemeVariant.Light force one.
			RequestedThemeVariant = ThemeVariant.Default;
			Styles.Add(new FluentTheme());
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.MainWindow = new MainWindow();
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
