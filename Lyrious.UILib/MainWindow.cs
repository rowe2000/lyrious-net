using Avalonia.Controls;

namespace Lyrious.UILib
{
	public class MainWindow : Window
	{
		public MainWindow()
		{
			Title = "Lyrious";
			Content = new MainView();
		}
	}
}
