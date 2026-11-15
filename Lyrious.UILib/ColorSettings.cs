using System;
using Avalonia;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// The colours the user has picked (Settings → Colors), kept in <see cref="App.Settings"/>. Each is
	/// null for the theme's own. <see cref="Apply"/> puts them into the app's resources, so everything
	/// showing them changes at once.
	/// </summary>
	public static class ColorSettings
	{
		/// <summary>
		/// The highlight: the current song, setlist and band (and other accent-coloured buttons), and
		/// the outline of a selected row. Null for the system's accent colour.
		/// </summary>
		public static Color? Highlight
		{
			get => Read(SettingKeys.HighlightColor);
			set => Write(SettingKeys.HighlightColor, value);
		}

		/// <summary>Chords above the lyrics, and the bar beside a chorus. Null for the theme's (blue).</summary>
		public static Color? Chord
		{
			get => Read(SettingKeys.ChordColor);
			set => Write(SettingKeys.ChordColor, value);
		}

		/// <summary>Level 1 headings in the lyrics ("# Title"). Null for the text colour.</summary>
		public static Color? Heading1
		{
			get => Read(SettingKeys.Heading1Color);
			set => Write(SettingKeys.Heading1Color, value);
		}

		/// <summary>Level 2 headings in the lyrics ("## Verse"). Null for the text colour.</summary>
		public static Color? Heading2
		{
			get => Read(SettingKeys.Heading2Color);
			set => Write(SettingKeys.Heading2Color, value);
		}

		/// <summary>The other headings in the lyrics (levels 3 to 6). Null for the text colour.</summary>
		public static Color? Heading
		{
			get => Read(SettingKeys.HeadingColor);
			set => Write(SettingKeys.HeadingColor, value);
		}

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
			var highlight = Highlight;
			foreach (var (key, amount) in AccentShades)
			{
				if (highlight is { } color)
				{
					resources[key] = Shade(color, amount);
				}
				else
				{
					resources.Remove(key);
				}
			}

			// Top-level entries win over the light/dark ones in LyriousTheme.
			SetBrush(resources, LyriousTheme.SelectionBrush, highlight);
			SetBrush(resources, LyriousTheme.ChordBrush, Chord);
			SetBrush(resources, LyriousTheme.Heading1Brush, Heading1);
			SetBrush(resources, LyriousTheme.Heading2Brush, Heading2);
			SetBrush(resources, LyriousTheme.HeadingBrush, Heading);
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
		}
	}
}
