using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's own theme resources, with light and dark variants. Look them up with
	/// <c>control.GetResourceObservable(LyriousTheme.ErrorBrush)</c> so they follow theme changes.
	/// </summary>
	public static class LyriousTheme
	{
		public const string ErrorBrush = "LyriousErrorBrush";
		public const string CardBrush = "LyriousCardBrush";
		public const string SuccessBrush = "LyriousSuccessBrush";
		/// <summary>Warnings: something works, but not as one might expect (yellow).</summary>
		public const string WarningBrush = "LyriousWarningBrush";
		/// <summary>Chords shown above the lyrics (ChordPro), and the bar beside a chorus.</summary>
		public const string ChordBrush = "LyriousChordBrush";
		/// <summary>Level 1 headings in the lyrics ("# Title"); white in the dark theme, the text colour in the light one, unless the user picks one.</summary>
		public const string Heading1Brush = "LyriousHeading1Brush";
		/// <summary>Level 2 headings in the lyrics ("## Verse"); dark gold in the dark theme, the text colour in the light one, unless the user picks one.</summary>
		public const string Heading2Brush = "LyriousHeading2Brush";
		/// <summary>The other headings in the lyrics (levels 3 to 6); amber in the dark theme, the text colour in the light one, unless the user picks one.</summary>
		public const string HeadingBrush = "LyriousHeadingBrush";
		public const string ForegroundBrush = "LyriousForegroundBrush";
		/// <summary>The outline of a selected list row (the current item is filled with the accent colour instead).</summary>
		public const string SelectionBrush = "LyriousSelectionBrush";
		/// <summary>The drawers' background.</summary>
		public const string DrawerBrush = "LyriousDrawerBrush";
		/// <summary>A drawer item's background when it isn't highlighted: a little apart from the drawer's.</summary>
		public const string DrawerItemBrush = "LyriousDrawerItemBrush";

		public static void AddTo(IResourceDictionary resources)
		{
			var light = new ResourceDictionary
			{
				[ErrorBrush] = new SolidColorBrush(Color.Parse("#C42B1C")),
				[CardBrush] = new SolidColorBrush(Color.Parse("#F3F3F3")),
				[SuccessBrush] = new SolidColorBrush(Color.Parse("#0F7B0F")),
				// A dark yellow, so it stays readable on a light background.
				[WarningBrush] = new SolidColorBrush(Color.Parse("#9D7A00")),
				[ChordBrush] = new SolidColorBrush(Color.Parse("#0067C0")),
				[Heading1Brush] = new SolidColorBrush(Color.Parse("#1B1B1B")),
				[Heading2Brush] = new SolidColorBrush(Color.Parse("#1B1B1B")),
				[HeadingBrush] = new SolidColorBrush(Color.Parse("#1B1B1B")),
				[ForegroundBrush] = new SolidColorBrush(Color.Parse("#1B1B1B")),
				[SelectionBrush] = new SolidColorBrush(Color.Parse("#A23BFF")),
				[DrawerBrush] = new SolidColorBrush(Color.Parse("#E4E4E4")),
				[DrawerItemBrush] = new SolidColorBrush(Color.Parse("#99FFFFFF")),
			};
			var dark = new ResourceDictionary
			{
				[ErrorBrush] = new SolidColorBrush(Color.Parse("#FF99A4")),
				[CardBrush] = new SolidColorBrush(Color.Parse("#2B2B2B")),
				[SuccessBrush] = new SolidColorBrush(Color.Parse("#6CCB5F")),
				[WarningBrush] = new SolidColorBrush(Color.Parse("#FCE100")),
				[ChordBrush] = new SolidColorBrush(Color.Parse("#FDBD00")),
				[Heading1Brush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
				[Heading2Brush] = new SolidColorBrush(Color.Parse("#967209")),
				[HeadingBrush] = new SolidColorBrush(Color.Parse("#D6A308")),
				[ForegroundBrush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
				[SelectionBrush] = new SolidColorBrush(Color.Parse("#A23BFF")),
				[DrawerBrush] = new SolidColorBrush(Color.Parse("#151515")),
				[DrawerItemBrush] = new SolidColorBrush(Color.Parse("#99000000")),
			};
			DisabledButtons(light, Color.Parse("#1B1B1B"));
			DisabledButtons(dark, Color.Parse("#FFFFFF"));
			// Pointed at and pressed, a button stands out more from the page, never less: lighter in the dark
			// theme (more white) and darker in the light one (more black). Darkening it in the dark theme, or
			// making it white in the light one, gave it the page's own colour, so it seemed to vanish.
			PointedAtButtons(light, Color.FromArgb(0x4D, 0, 0, 0), Color.FromArgb(0x66, 0, 0, 0));
			PointedAtButtons(dark, Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
			resources.ThemeDictionaries[ThemeVariant.Light] = light;
			resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
		}

		/// <summary>The fill of plain, repeat and (unchecked) toggle buttons pointed at, and pressed.</summary>
		private static void PointedAtButtons(ResourceDictionary theme, Color pointedAt, Color pressed)
		{
			foreach (var button in new[] { "Button", "RepeatButton", "ToggleButton" })
			{
				theme[button + "BackgroundPointerOver"] = new SolidColorBrush(pointedAt);
				theme[button + "BackgroundPressed"] = new SolidColorBrush(pressed);
			}
		}

		/// <summary>
		/// A button that can't be used has no background or border, and its text (and icon) in a dimmed
		/// text colour: plain, accent, repeat and toggle buttons (checked or not), in place of the theme's
		/// grey box.
		/// </summary>
		private static void DisabledButtons(ResourceDictionary theme, Color text)
		{
			var none = new SolidColorBrush(Colors.Transparent);
			var dimmed = new SolidColorBrush(text, 0.4);
			foreach (var button in new[] { "Button", "AccentButton", "RepeatButton", "ToggleButton" })
			{
				theme[button + "BackgroundDisabled"] = none;
				theme[button + "BorderBrushDisabled"] = none;
				theme[button + "ForegroundDisabled"] = dimmed;
			}

			foreach (var state in new[] { "Checked", "Indeterminate" })
			{
				theme["ToggleButtonBackground" + state + "Disabled"] = none;
				theme["ToggleButtonBorderBrush" + state + "Disabled"] = none;
				theme["ToggleButtonForeground" + state + "Disabled"] = dimmed;
			}
		}
	}
}
