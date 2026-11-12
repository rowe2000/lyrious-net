using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace Lyrious.UILib
{
	public class MainWindow : Window
	{
		public MainWindow()
		{
            Title = "Lyrious";
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new Rectangle() },
            };
}
	}
}
