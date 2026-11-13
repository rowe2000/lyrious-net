using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The playback toolbar under the lyrics: previous and next song in the setlist, play/pause
	/// for scrolling the lyrics by themselves, and the scroll speed (− and +). It stays visible in
	/// focus mode. Icons are drawn as shapes, since the browser has no symbol fonts.
	/// </summary>
	public sealed class PlaybackBar : UserControl
	{
		private const string PreviousIcon = "M6,5 V19 M18,5 L8,12 L18,19 Z";
		private const string NextIcon = "M18,5 V19 M6,5 L16,12 L6,19 Z";
		private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
		private const string PauseIcon = "M8,5 V19 M16,5 V19";
		private const string SlowerIcon = "M5,12 H19";
		private const string FasterIcon = "M12,5 V19 M5,12 H19";

		/// <summary>The speeds to step through with − and +, as multiples of the normal speed.</summary>
		private static readonly double[] Speeds = [0.5, 0.75, 1, 1.25, 1.5, 2, 2.5, 3, 4];

		private readonly Button previous = HomeView.IconButton(PreviousIcon, "Previous song");
		private readonly Button next = HomeView.IconButton(NextIcon, "Next song");
		private readonly Button playPause = HomeView.IconButton(PlayIcon, "Scroll the lyrics");
		private readonly Button slower = HomeView.IconButton(SlowerIcon, "Scroll slower");
		private readonly Button faster = HomeView.IconButton(FasterIcon, "Scroll faster");
		private readonly TextBlock speedText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center, Opacity = 0.8 };
		private int speedIndex = Array.IndexOf(Speeds, 1.0);

		/// <summary>Raised when the user wants the previous song in the setlist.</summary>
		public event Action? PreviousRequested;

		/// <summary>Raised when the user wants the next song in the setlist.</summary>
		public event Action? NextRequested;

		/// <summary>Raised when the user presses play/pause.</summary>
		public event Action? PlayPauseRequested;

		/// <summary>Raised when the speed changes; see <see cref="Speed"/>.</summary>
		public event Action? SpeedChanged;

		/// <summary>The scroll speed as a multiple of the normal speed.</summary>
		public double Speed => Speeds[speedIndex];

		public PlaybackBar()
		{
			previous.Click += (_, _) => PreviousRequested?.Invoke();
			next.Click += (_, _) => NextRequested?.Invoke();
			playPause.Click += (_, _) => PlayPauseRequested?.Invoke();
			slower.Click += (_, _) => ChangeSpeed(-1);
			faster.Click += (_, _) => ChangeSpeed(+1);
			UpdateSpeed();
			SetNavigation(false, false);

			var buttons = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				HorizontalAlignment = HorizontalAlignment.Center,
				Children = { previous, slower, playPause, faster, speedText, next },
			};

			var bar = new Border { Padding = new Thickness(8, 4), Child = buttons };
			bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(LyriousTheme.CardBrush));
			Content = bar;
		}

		/// <summary>Shows play or pause, for whether the lyrics are scrolling.</summary>
		public void SetPlaying(bool playing)
		{
			((Path)playPause.Content!).Data = Geometry.Parse(playing ? PauseIcon : PlayIcon);
			var description = playing ? "Stop scrolling" : "Scroll the lyrics";
			ToolTip.SetTip(playPause, description);
			Avalonia.Automation.AutomationProperties.SetName(playPause, description);
		}

		/// <summary>Enables previous and next (only for a song shown from a setlist, and not past its ends).</summary>
		public void SetNavigation(bool canGoBack, bool canGoForward)
		{
			previous.IsEnabled = canGoBack;
			next.IsEnabled = canGoForward;
		}

		private void ChangeSpeed(int step)
		{
			speedIndex = Math.Clamp(speedIndex + step, 0, Speeds.Length - 1);
			UpdateSpeed();
			SpeedChanged?.Invoke();
		}

		private void UpdateSpeed()
		{
			speedText.Text = $"{Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}×";
			slower.IsEnabled = speedIndex > 0;
			faster.IsEnabled = speedIndex < Speeds.Length - 1;
		}
	}
}
