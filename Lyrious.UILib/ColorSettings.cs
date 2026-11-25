using System;
using Avalonia;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// The colours the user has picked (Settings → Colors), kept with the user (the same on all their
	/// devices, see <see cref="UserSettings"/>) and, as the last known copy, in <see cref="App.Settings"/>. Each is
	/// null for the theme's own. <see cref="Apply"/> puts them into the app's resources, so everything
	/// showing them changes at once.
	/// </summary>
	public static class ColorSettings
	{
		/// <summary>
		/// The highlight: the current song, setlist and band (and other accent-coloured buttons), and
		/// the outline of a selected row. Null for <see cref="DefaultHighlight"/>.
		/// </summary>
		public static Color? Highlight
		{
			get => Read(SettingKeys.HighlightColor);
			// The default purple picked by hand is the default: nothing kept.
			set => Write(SettingKeys.HighlightColor, value == DefaultHighlight ? null : value);
		}

		/// <summary>Chords above the lyrics, and the bar beside a chorus. Null for the theme's (yellow in the dark theme, blue in the light one).</summary>
		public static Color? Chord
		{
			get => Read(SettingKeys.ChordColor);
			set => Write(SettingKeys.ChordColor, value);
		}

		/// <summary>Level 1 headings in the lyrics ("# Title"). Null for the theme's (white in the dark theme, the text colour in the light one).</summary>
		public static Color? Heading1
		{
			get => Read(SettingKeys.Heading1Color);
			set => Write(SettingKeys.Heading1Color, value);
		}

		/// <summary>Level 2 headings in the lyrics ("## Verse"). Null for the theme's (dark gold in the dark theme, the text colour in the light one).</summary>
		public static Color? Heading2
		{
			get => Read(SettingKeys.Heading2Color);
			set => Write(SettingKeys.Heading2Color, value);
		}

		/// <summary>The other headings in the lyrics (levels 3 to 6). Null for the theme's (amber in the dark theme, the text colour in the light one).</summary>
		public static Color? Heading
		{
			get => Read(SettingKeys.HeadingColor);
			set => Write(SettingKeys.HeadingColor, value);
		}

		/// <summary>The highlight unless the user picks one (purple, in both themes, instead of the system's accent colour).</summary>
		public static readonly Color DefaultHighlight = Color.Parse("#A23BFF");

		// Fluent's accent shades: the light theme fills with Dark1, the dark theme with Light2, and so on.
		private static readonly (string Key, double Amount)[] AccentShades =
		[
			("SystemAccentColor", 0),
			("SystemAccentColorLight1", 0.2), ("SystemAccentColorLight2", 0.4), ("SystemAccentColorLight3", 0.6),
			("SystemAccentColorDark1", -0.2), ("SystemAccentColorDark2", -0.4), ("SystemAccentColorDark3", -0.6),
		];

		/// <summary>Puts the chosen colours into the app's resources (or takes them out, for the theme's own).</summary>
		public static void Apply()
		{
			if (Application.Current is not { } app)
			{
				return;
			}

			var resources = app.Resources;
			// The accent is always set: the user's highlight, or ours rather than the system's.
			var highlight = Highlight ?? DefaultHighlight;
			foreach (var (key, amount) in AccentShades)
			{
				resources[key] = Shade(highlight, amount);
			}

			// The text and icons on the highlight (highlighted buttons, the current row, checked toggles):
			// black on a light fill, white on a dark one. So they can be read whatever highlight was chosen.
			SetOnHighlight(resources, Avalonia.Styling.ThemeVariant.Light, highlight);
			SetOnHighlight(resources, Avalonia.Styling.ThemeVariant.Dark, highlight);

			// Highlighted buttons and checked toggles are the highlight itself (in both themes); pointed at
			// and pressed, a step (and two) darker in the dark theme, lighter in the light one.
			SetHighlightFill(resources, Avalonia.Styling.ThemeVariant.Light, highlight, Shade(highlight, 0.15), Shade(highlight, 0.3));
			SetHighlightFill(resources, Avalonia.Styling.ThemeVariant.Dark, highlight, Shade(highlight, -0.15), Shade(highlight, -0.3));

			// Top-level entries win over the light/dark ones in LyriousTheme.
			SetBrush(resources, LyriousTheme.SelectionBrush, Highlight);
			SetBrush(resources, LyriousTheme.ChordBrush, Chord);
			SetBrush(resources, LyriousTheme.Heading1Brush, Heading1);
			SetBrush(resources, LyriousTheme.Heading2Brush, Heading2);
			SetBrush(resources, LyriousTheme.HeadingBrush, Heading);
		}

		// The theme's brushes for what's drawn on the accent fill (Fluent's), each state alike.
		private static readonly string[] OnHighlightKeys =
		[
			"AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
			"ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed",
		];

		/// <summary>
		/// The theme's fills of the highlight (highlighted buttons, checked toggles): as they are, pointed at
		/// and pressed (see <see cref="Apply"/>).
		/// </summary>
		private static void SetHighlightFill(Avalonia.Controls.IResourceDictionary resources, Avalonia.Styling.ThemeVariant theme, Color normal, Color pointedAt, Color pressed)
		{
			var dictionary = ThemeDictionary(resources, theme);
			foreach (var (state, color) in new[] { ("", normal), ("PointerOver", pointedAt), ("Pressed", pressed) })
			{
				var brush = new SolidColorBrush(color);
				dictionary["AccentButtonBackground" + state] = brush;
				dictionary["AccentButtonBorderBrush" + state] = brush;
				dictionary["ToggleButtonBackgroundChecked" + state] = brush;
				dictionary["ToggleButtonBorderBrushChecked" + state] = brush;
			}
		}

		/// <summary>The app's resources for one theme (made the first time).</summary>
		private static Avalonia.Controls.IResourceDictionary ThemeDictionary(Avalonia.Controls.IResourceDictionary resources, Avalonia.Styling.ThemeVariant theme)
		{
			if (!resources.ThemeDictionaries.TryGetValue(theme, out var provider) || provider is not Avalonia.Controls.IResourceDictionary dictionary)
			{
				var created = new Avalonia.Controls.ResourceDictionary();
				resources.ThemeDictionaries[theme] = created;
				dictionary = created;
			}

			return dictionary;
		}

		/// <summary>The theme's colour for text and icons on a fill of the highlight: black or white, whichever reads better.</summary>
		private static void SetOnHighlight(Avalonia.Controls.IResourceDictionary resources, Avalonia.Styling.ThemeVariant theme, Color fill)
		{
			if (!resources.ThemeDictionaries.TryGetValue(theme, out var provider) || provider is not Avalonia.Controls.IResourceDictionary dictionary)
			{
				var created = new Avalonia.Controls.ResourceDictionary();
				resources.ThemeDictionaries[theme] = created;
				dictionary = created;
			}

			var light = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255 > 0.6;
			var brush = new SolidColorBrush(light ? Colors.Black : Colors.White);
			foreach (var key in OnHighlightKeys)
			{
				dictionary[key] = brush;
			}
		}

		/// <summary>"#RRGGBB" for a colour.</summary>
		public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

		/// <summary>A colour from "#RRGGBB" (or "RRGGBB", or a name such as "teal"); null if it isn't one.</summary>
		public static Color? TryParse(string? text)
		{
			var value = text?.Trim();
			if (string.IsNullOrEmpty(value))
			{
				return null;
			}

			if (Color.TryParse(value, out var color) || Color.TryParse("#" + value, out color))
			{
				return Color.FromRgb(color.R, color.G, color.B); // No transparency.
			}

			return null;
		}

		private static void SetBrush(Avalonia.Controls.IResourceDictionary resources, string key, Color? color)
		{
			if (color is { } value)
			{
				resources[key] = new SolidColorBrush(value);
			}
			else
			{
				resources.Remove(key);
			}
		}

		/// <summary>Towards white (positive amount) or black (negative).</summary>
		private static Color Shade(Color color, double amount)
		{
			byte Mix(byte channel) => (byte)Math.Round(amount >= 0 ? channel + (255 - channel) * amount : channel * (1 + amount));
			return Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));
		}

		private static Color? Read(string key) => TryParse(App.Settings.Get(key));

		private static void Write(string key, Color? color)
		{
			App.Settings.Set(key, color is { } value ? ToHex(value) : null);
			Apply();
			UserSettings.ChangedHere();
		}
	}
}
