using Avalonia.Controls;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib
{
	public class MainWindow : Window
	{
		public MainWindow()
		{
			Title = L.T("Lyrious");
			Content = UiScale.Wrap(new MainView());
		}
	}
}
