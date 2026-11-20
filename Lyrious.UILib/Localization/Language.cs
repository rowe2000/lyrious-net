using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lyrious.UILib.Localization
{
	/// <summary>
	/// The language the app is shown in: the one chosen in Settings (kept with the user, see
	/// <see cref="UserSettings"/>), else the device's (Swedish for a device in Swedish, else English).
	/// Dates, times and numbers follow it too.
	/// </summary>
	public static class Language
	{
		/// <summary>A language to choose: its code (empty: the device's) and its name, in itself.</summary>
		public sealed record Choice(string Code, string Name);

		/// <summary>The choices in Settings.</summary>
		public static IReadOnlyList<Choice> Choices { get; } =
		[
			new("", "Same as the device"),
			new("en", "English"),
			new("sv", "Svenska"),
		];

		/// <summary>The choice saved (its code; empty for the device's language).</summary>
		public static string Chosen
		{
			get => App.Settings.Get(SettingKeys.Language) ?? "";
			set
			{
				App.Settings.Set(SettingKeys.Language, string.IsNullOrEmpty(value) ? null : value);
				// Kept with the user: sent to the server, and marked as waiting first, so the sync when
				// the screens are made anew (below) sends it instead of taking the server's older copy.
				UserSettings.ChangedHere();
				if (Apply())
				{
					Changed?.Invoke();
				}
			}
		}

		/// <summary>The language shown now: "sv" or "en".</summary>
		public static string Current { get; private set; } = "en";

		/// <summary>Raised when the language shown changes (the screens are then made anew in it).</summary>
		public static event Action? Changed;

		/// <summary>
		/// Shows the app in the chosen language (or the device's): its texts, and its dates, times and
		/// numbers. True if that's another language than before.
		/// </summary>
		public static bool Apply()
		{
			var code = Chosen is "sv" or "en"
				? Chosen
				: CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "sv" || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sv" ? "sv" : "en";
			var changed = code != Current;
			Current = code;
			L.Use(code == "sv" ? Swedish.Texts : new Dictionary<string, string>());

			// Swedish dates and numbers in Swedish; in English, the device's own way (as before).
			var culture = code == "sv" ? SwedishCulture() : DeviceCulture;
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;
			CultureInfo.DefaultThreadCurrentCulture = culture;
			CultureInfo.DefaultThreadCurrentUICulture = culture;
			return changed;
		}

		/// <summary>
		/// The language a change from another device asked for, applied now (e.g. once the lyrics
		/// stopped playing); raises <see cref="Changed"/> if the language shown changes.
		/// </summary>
		public static void ApplyChanged()
		{
			if (Apply())
			{
				Changed?.Invoke();
			}
		}

		/// <summary>Swedish dates and numbers; the device's own if this platform has no Swedish culture data.</summary>
		private static CultureInfo SwedishCulture()
		{
			try
			{
				return CultureInfo.GetCultureInfo("sv-SE");
			}
			catch (CultureNotFoundException)
			{
				return DeviceCulture;
			}
		}

		// The device's own culture, as it was when the app started (before any choice changed it).
		private static readonly CultureInfo DeviceCulture = CultureInfo.CurrentCulture;
	}
}
