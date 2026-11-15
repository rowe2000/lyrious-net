using Avalonia.Controls;

namespace Lyrious.UILib
{
	public class MainWindow : Window
	{
		public MainWindow()
		{
			Title = "Lyrious";
			Content = UiScale.Wrap(new MainView());
		}
	}
}
