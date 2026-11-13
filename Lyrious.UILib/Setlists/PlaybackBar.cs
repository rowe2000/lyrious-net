using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The playback toolbar under the lyrics. At the left, the next song in the setlist, shown by
	/// name under a "Next" caption (tap to go to it); at the right, back to the start of the song, and play/pause for
	/// scrolling the lyrics by themselves. It stays visible in focus mode. Icons are drawn as shapes, since the browser has
	/// no symbol fonts.
	/// </summary>
	public sealed class PlaybackBar : UserControl
	{
		// A bar with a triangle pointing at it: back to the start.
		private const string RestartIcon = "M6,5 V19 M18,5 L8,12 L18,19 Z";
		private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
		private const string PauseIcon = "M8,5 V19 M16,5 V19";

		private readonly Button restart = HomeView.IconButton(RestartIcon, "Back to the start of the song");
		private readonly Button playPause = HomeView.IconButton(PlayIcon, "Scroll the lyrics");
		// Where the next song would be, after the last song of a setlist.
		private readonly TextBlock noMoreSongs = new() { Text = "No more songs", FontSize = 28, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0), IsVisible = false };
		private readonly TextBlock nextInfo = new() { FontSize = 14, Opacity = 0.7, Margin = new Thickness(16, 0, 0, 0) };
		private readonly TextBlock nextName = new() { FontSize = 28, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
		private readonly Button next;

		/// <summary>Raised when the user wants the lyrics back at the start of the song.</summary>
		public event Action? RestartRequested;

		/// <summary>Raised when the user wants the next song in the setlist.</summary>
		public event Action? NextRequested;

		/// <summary>Raised when the user presses play/pause.</summary>
		public event Action? PlayPauseRequested;

		public PlaybackBar()
		{
			// Twice the size of the app's other icon buttons, to hit easily while playing.
			Enlarge(restart);
			Enlarge(playPause);

			// As tall as the icon buttons (48-pixel icon plus 16 padding above and below).
			next = new Button
			{
				// A small line above the song's name: "Next" at the left, key · tempo · beat at the right.
				Content = new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Children = { CaptionLine(), nextName },
				},
				MinHeight = 80,
				Padding = new Thickness(16, 0),
				HorizontalAlignment = HorizontalAlignment.Left,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
			};

			restart.Click += (_, _) => RestartRequested?.Invoke();
			next.Click += (_, _) => NextRequested?.Invoke();
			playPause.Click += (_, _) => PlayPauseRequested?.Invoke();
			SetNextSong(null);

			// Play at the far right with back-to-start just left of it; the next song fills the rest from the left.
			DockPanel.SetDock(playPause, Dock.Right);
			DockPanel.SetDock(restart, Dock.Right);
			restart.Margin = new Thickness(8, 0);
			var row = new DockPanel { Children = { playPause, restart, new Panel { Children = { next, noMoreSongs } } } };

			var bar = new Border { Padding = new Thickness(16, 8), Child = row };
			bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(LyriousTheme.CardBrush));
			Content = bar;
		}

		private Control CaptionLine()
		{
			DockPanel.SetDock(nextInfo, Dock.Right);
			return new DockPanel { Children = { nextInfo, new TextBlock { Text = "Next", FontSize = 14, Opacity = 0.7 } } };
		}

		/// <summary>
		/// Doubles an icon button: the whole 24-pixel icon box is scaled to 48 (so every icon keeps its
		/// proportions and line width relative to its size), with twice the padding.
		/// </summary>
		private static void Enlarge(Button button)
		{
			button.Padding = new Thickness(16);
			if (button.Content is Path icon)
			{
				// Detach the icon from the button first: a control can only have one parent.
				button.Content = null;
				button.Content = new Viewbox { Width = 48, Height = 48, Child = icon };
			}
		}

		/// <summary>Shows play or pause, for whether the lyrics are scrolling.</summary>
		public void SetPlaying(bool playing)
		{
			var icon = playPause.Content is Viewbox { Child: Path inBox } ? inBox : (Path)playPause.Content!;
			icon.Data = Geometry.Parse(playing ? PauseIcon : PlayIcon);
			var description = playing ? "Stop scrolling" : "Scroll the lyrics";
			ToolTip.SetTip(playPause, description);
			Avalonia.Automation.AutomationProperties.SetName(playPause, description);
		}

		/// <summary>
		/// Shows the next song's name and its key, tempo and beat. After the last song of a setlist
		/// "No more songs" is shown there instead; for a song shown from the songbook, nothing.
		/// </summary>
		/// <param name="lastInSetlist">The current song is the setlist's last: "No more songs" is shown instead.</param>
		public void SetNextSong(string? nextSongName, string? details = null, bool lastInSetlist = false)
		{
			next.IsVisible = nextSongName != null;
			noMoreSongs.IsVisible = nextSongName == null && lastInSetlist;
			nextName.Text = nextSongName;
			nextInfo.Text = details;
			var description = nextSongName == null ? "Next song" : $"Next song: {nextSongName}";
			ToolTip.SetTip(next, description);
			Avalonia.Automation.AutomationProperties.SetName(next, description);
		}
	}
}
