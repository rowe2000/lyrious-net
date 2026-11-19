using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>About the app: its name, version and what it's for, and the release notes, newest first.</summary>
	public sealed class AboutView : UserControl
	{
		public AboutView()
		{
			var column = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					new TextBlock { Text = "Lyrious", FontSize = 24, FontWeight = FontWeight.SemiBold },
					new TextBlock { Text = $"Version {ReleaseNotes.AppVersion}", Opacity = 0.7 },
					new TextBlock
					{
						Text = "Setlists, songbooks and lyrics for bands: lyrics and chords that scroll in time with the song, shared and followed live by the whole band.",
						TextWrapping = TextWrapping.Wrap,
					},
					new TextBlock { Text = "Release notes", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) },
				},
			};

			foreach (var note in ReleaseNotes.All)
			{
				column.Children.Add(OnboardingView.ReleaseNoteBlock(note));
			}

			Content = PageLayout.Page(
				"About",
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				closable: true,
				icon: AppIcons.About);
		}
	}
}
