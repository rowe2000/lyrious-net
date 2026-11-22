using Avalonia;
using Avalonia.Styling;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's theme, chosen in Settings and kept with the user (see <see cref="UserSettings"/>): dark
	/// (unless chosen otherwise), light, or the same as the computer (following it when it changes).
	/// </summary>
	public static class ThemeSettings
	{
		public const string Dark = "dark";
		public const string Light = "light";
		public const string System = "system";

		/// <summary>The theme chosen: <see cref="Dark"/>, <see cref="Light"/> or <see cref="System"/>.</summary>
		public static string Chosen
		{
			get => App.Settings.Get(SettingKeys.Theme) switch
			{
				Light => Light,
				System => System,
				_ => Dark,
			};
			set
			{
				// Dark is the default, so it isn't stored.
				App.Settings.Set(SettingKeys.Theme, value is Light or System ? value : null);
				// Kept with the user: sent to the server (and waiting first, so a sync doesn't take an older copy).
				UserSettings.ChangedHere();
				Apply();
			}
		}

		/// <summary>Shows the app in the theme chosen.</summary>
		public static void Apply()
		{
			if (Application.Current is { } app)
			{
				app.RequestedThemeVariant = Chosen switch
				{
					Light => ThemeVariant.Light,
					System => ThemeVariant.Default,
					_ => ThemeVariant.Dark,
				};
			}
		}
	}
}
