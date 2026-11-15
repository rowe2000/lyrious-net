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
		/// <summary>Level 1 headings in the lyrics ("# Title"); the text colour unless the user picks one.</summary>
		public const string Heading1Brush = "LyriousHeading1Brush";
		/// <summary>Level 2 headings in the lyrics ("## Verse"); the text colour unless the user picks one.</summary>
		public const string Heading2Brush = "LyriousHeading2Brush";
		/// <summary>The other headings in the lyrics (levels 3 to 6); the text colour unless the user picks one.</summary>
		public const string HeadingBrush = "LyriousHeadingBrush";
		public const string ForegroundBrush = "LyriousForegroundBrush";
		/// <summary>The outline of a selected list row (the current item is filled with the accent colour instead).</summary>
		public const string SelectionBrush = "LyriousSelectionBrush";

		public static void AddTo(IResourceDictionary resources)
		{
			resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
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
				[SelectionBrush] = new SolidColorBrush(Color.Parse("#0067C0")),
			};
			resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
			{
				[ErrorBrush] = new SolidColorBrush(Color.Parse("#FF99A4")),
				[CardBrush] = new SolidColorBrush(Color.Parse("#2B2B2B")),
				[SuccessBrush] = new SolidColorBrush(Color.Parse("#6CCB5F")),
				[WarningBrush] = new SolidColorBrush(Color.Parse("#FCE100")),
				[ChordBrush] = new SolidColorBrush(Color.Parse("#60CDFF")),
				[Heading1Brush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
				[Heading2Brush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
				[HeadingBrush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
				[ForegroundBrush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
				[SelectionBrush] = new SolidColorBrush(Color.Parse("#60CDFF")),
			};
		}
	}
}
