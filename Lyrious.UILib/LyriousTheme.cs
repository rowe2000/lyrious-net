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
		public const string ForegroundBrush = "LyriousForegroundBrush";

		public static void AddTo(IResourceDictionary resources)
		{
			resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
			{
				[ErrorBrush] = new SolidColorBrush(Color.Parse("#C42B1C")),
				[CardBrush] = new SolidColorBrush(Color.Parse("#F3F3F3")),
				[SuccessBrush] = new SolidColorBrush(Color.Parse("#0F7B0F")),
				[ForegroundBrush] = new SolidColorBrush(Color.Parse("#1B1B1B")),
			};
			resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
			{
				[ErrorBrush] = new SolidColorBrush(Color.Parse("#FF99A4")),
				[CardBrush] = new SolidColorBrush(Color.Parse("#2B2B2B")),
				[SuccessBrush] = new SolidColorBrush(Color.Parse("#6CCB5F")),
				[ForegroundBrush] = new SolidColorBrush(Color.Parse("#FFFFFF")),
			};
		}
	}
}
