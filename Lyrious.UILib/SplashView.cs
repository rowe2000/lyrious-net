using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

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
			HorizontalAlignment = HorizontalAlignment.Center;
			VerticalAlignment = VerticalAlignment.Center;
			Margin = new Thickness(16);
			Children.Add(AppLogo.Create(200));
			Children.Add(new TextBlock { Text = text, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center });
			Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, HorizontalAlignment = HorizontalAlignment.Center });
		}
	}
}
