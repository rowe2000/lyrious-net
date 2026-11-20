using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lyrious.UILib.Localization;

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
					new TextBlock { Text = L.T("Lyrious"), FontSize = 24, FontWeight = FontWeight.SemiBold },
					new TextBlock { Text = L.T("Version {0}", ReleaseNotes.AppVersion), Opacity = 0.7 },
					new TextBlock
					{
						Text = L.T("Branch {0}", ReleaseNotes.AppBranch ?? ""),
						Opacity = 0.7,
						IsVisible = ReleaseNotes.AppBranch is not null,
					},
					new TextBlock
					{
						Text = L.T("Commit {0}", ReleaseNotes.AppCommit ?? ""),
						Opacity = 0.7,
						IsVisible = ReleaseNotes.AppCommit is not null,
					},
					new TextBlock
					{
						Text = L.T("Setlists, songbooks and lyrics for bands: lyrics and chords that scroll in time with the song, shared and followed live by the whole band."),
						TextWrapping = TextWrapping.Wrap,
					},
					new TextBlock { Text = L.T("Release notes"), FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) },
				},
			};

			foreach (var note in ReleaseNotes.All)
			{
				column.Children.Add(OnboardingView.ReleaseNoteBlock(note));
			}

			Content = PageLayout.Page(
				L.T("About"),
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				closable: true,
				icon: AppIcons.About);
		}
	}
}
