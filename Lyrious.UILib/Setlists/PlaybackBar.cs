using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The playback toolbar under the lyrics. At the left, the next song in the setlist, shown by
	/// name under a "Next" caption (tap to go to it); at the right, back to the start of the song, and play/pause for
	/// scrolling the lyrics by themselves, and before them a globe that turns band sync on and off. It stays visible in focus mode. Icons are drawn as shapes, since the browser has
	/// no symbol fonts.
	/// </summary>
	public sealed class PlaybackBar : UserControl
	{
		// A bar with a triangle pointing at it: back to the start.
		private const string RestartIcon = "M6,5 V19 M18,5 L8,12 L18,19 Z";
		private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
		private const string PauseIcon = "M8,5 V19 M16,5 V19";
		// A globe: the outline, the equator and a meridian.
		// The caption above the next song's name: half of 14 would be too small to read.
		private const double CaptionSize = 10;
		private const string GlobeIcon = "M12,3 A9,9 0 1,1 11.99,3 Z M3,12 H21 M12,3 C6.5,7 6.5,17 12,21 M12,3 C17.5,7 17.5,17 12,21";

		private readonly Button restart = HomeView.IconButton(RestartIcon, "Back to the start of the song");
		private readonly Button playPause = HomeView.IconButton(PlayIcon, "Scroll the lyrics");
		// Where the next song would be, after the last song of a setlist.
		private readonly TextBlock noMoreSongs = new() { Text = "No more songs", FontSize = 14, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), IsVisible = false };
		private readonly TextBlock nextInfo = new() { FontSize = CaptionSize, Opacity = 0.7, Margin = new Thickness(8, 0, 0, 0) };
		private readonly TextBlock nextName = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
		private readonly Button next;
		private readonly LongClickToggleButton bandSync = new()
		{
			Content = new Path
			{
				Data = Geometry.Parse(GlobeIcon),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 24,
				Height = 24,
			},
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
		};

		/// <summary>Raised when the user turns band sync on (true) or off (false).</summary>
		public event Action<bool>? BandSyncToggled;

		/// <summary>
		/// Raised on a longclick of the globe: band sync on, sending this member's setlist and song to
		/// the band (instead of taking the band's). The globe isn't toggled; call <see cref="SetBandSync"/>.
		/// </summary>
		public event Action? BandSyncSendRequested;

		/// <summary>Raised when the user wants the lyrics back at the start of the song.</summary>
		public event Action? RestartRequested;

		/// <summary>Raised when the user wants the next song in the setlist.</summary>
		public event Action? NextRequested;

		/// <summary>Raised when the user presses play/pause.</summary>
		public event Action? PlayPauseRequested;

		public PlaybackBar()
		{
			// The size of the app's other icon buttons.
			ToolbarSize(restart);
			ToolbarSize(playPause);

			// As tall as the icon buttons (24-pixel icon plus 8 padding above and below).
			next = new Button
			{
				// A small line above the song's name: "Next" at the left, key · tempo · beat at the right.
				Content = new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Children = { CaptionLine(), nextName },
				},
				MinHeight = 40,
				Padding = new Thickness(8, 0),
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

			// Band sync: checked, it's filled with the highlight colour (the toggle's own checked look).
			ToolbarSize(bandSync);
			ShowBandSync();
			bandSync.LongClick += () => BandSyncSendRequested?.Invoke();
			bandSync.IsCheckedChanged += (_, _) =>
			{
				ShowBandSync();
				if (!settingBandSync)
				{
					BandSyncToggled?.Invoke(bandSync.IsChecked == true);
				}
			};

			// Play at the far right with back-to-start just left of it; the next song fills the rest from the left.
			DockPanel.SetDock(playPause, Dock.Right);
			DockPanel.SetDock(restart, Dock.Right);
			DockPanel.SetDock(bandSync, Dock.Right);
			restart.Margin = new Thickness(4, 0);
			var row = new DockPanel { Children = { playPause, restart, bandSync, new Panel { Children = { next, noMoreSongs } } } };

			var bar = new Border { Padding = new Thickness(8, 4), Child = row };
			bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(LyriousTheme.CardBrush));
			Content = bar;
		}

		private Control CaptionLine()
		{
			DockPanel.SetDock(nextInfo, Dock.Right);
			return new DockPanel { Children = { nextInfo, new TextBlock { Text = "Next", FontSize = CaptionSize, Opacity = 0.7 } } };
		}

		/// <summary>
		/// Sizes a toolbar icon button: the icon in a 24-pixel box (so every icon keeps its proportions
		/// and line width), with 8 padding, like the app's other icon buttons.
		/// </summary>
		private static void ToolbarSize(Button button)
		{
			button.Padding = new Thickness(8);
			if (button.Content is Path icon)
			{
				// Detach the icon from the button first: a control can only have one parent.
				button.Content = null;
				button.Content = new Viewbox { Width = 24, Height = 24, Child = icon };
			}
		}

		/// <summary>Shows whether band sync is on, without raising <see cref="BandSyncToggled"/>.</summary>
		public void SetBandSync(bool on)
		{
			if ((bandSync.IsChecked == true) == on)
			{
				return;
			}

			settingBandSync = true;
			bandSync.IsChecked = on;
			settingBandSync = false;
		}

		// Set while SetBandSync changes the toggle, so it isn't reported as the user's change.
		private bool settingBandSync;

		/// <summary>The globe's colour (readable on the highlight when on) and its description.</summary>
		private void ShowBandSync()
		{
			var on = bandSync.IsChecked == true;
			var icon = bandSync.Content is Viewbox { Child: Path inBox } ? inBox : (Path)bandSync.Content!;
			icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable(on ? "ToggleButtonForegroundChecked" : LyriousTheme.ForegroundBrush));
			var description = on
				? "Band sync is on: following and sending the band's song, setlist and play/stop. Tap to turn off; hold to send your setlist and song to the band."
				: "Band sync is off: not following or sending the band's song, setlist and play/stop. Tap to turn on and take the band's; hold to turn on and send yours.";
			ToolTip.SetTip(bandSync, description);
			Avalonia.Automation.AutomationProperties.SetName(bandSync, on ? "Band sync on" : "Band sync off");
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
		/// <summary>Greys out the next song while playing: the song can't be changed then.</summary>
		public void SetNextAllowed(bool allowed)
		{
			next.IsEnabled = allowed;
		}

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
