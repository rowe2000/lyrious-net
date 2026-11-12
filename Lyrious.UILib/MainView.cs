using Avalonia.Controls;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's main content. Desktop shows it inside <see cref="MainWindow"/>; single-view hosts
	/// such as the browser, which have no windows, show it directly.
	/// </summary>
	public class MainView : UserControl
	{
		public MainView()
		{
			Content = "Lyrious";
		}
	}
}
