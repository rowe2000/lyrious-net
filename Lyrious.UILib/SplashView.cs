using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// What shows while the app gets ready (logging in, the first sync): the singer, what's going on,
	/// and a progress bar, in the middle.
	/// </summary>
	public sealed class SplashView : StackPanel
	{
		public SplashView(string text)
		{
			Spacing = 16;
			// The whole width (each part in its middle), so the text has room to wrap in as few lines as it can.
			HorizontalAlignment = HorizontalAlignment.Stretch;
			VerticalAlignment = VerticalAlignment.Center;
			Margin = new Thickness(16);
			Children.Add(AppLogo.Create(200));
			// On a narrow screen on more lines (centred), not cut at the edges.
			Children.Add(new TextBlock
			{
				Text = text,
				FontSize = 18,
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center,
			});
			Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, HorizontalAlignment = HorizontalAlignment.Center });
		}
	}
}
