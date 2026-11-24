using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The playback toolbar under the lyrics. At the left, the next song in the setlist, shown by
	/// name under a "Next" caption (tap to go to it); at the right, back to the start of the song, and play/pause for
	/// scrolling the lyrics by themselves, and before them the band icon that turns band sync on and off. It stays visible in focus mode. Icons are drawn as shapes, since the browser has
	/// no symbol fonts. While a media file plays in the app (see <see cref="ISoundStore"/>), a bar above
	/// shows where it is, with the time played and its length; dragging it (or clicking on it) moves
	/// the sound there.
	/// </summary>
	public sealed class PlaybackBar : UserControl
	{
		// A bar with a triangle pointing at it: back to the start.
		private const string RestartIcon = "M6,5 V19 M18,5 L8,12 L18,19 Z";
		private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
		private const string PauseIcon = "M8,5 V19 M16,5 V19";
		// Slower and faster (the media file's speed): a turtle and a rabbit, side on.
		private const string TurtleIcon = "M4,16 C4,10 8,7 12,7 C16,7 19,10 19,16 Z M4,16 H19 M8.5,16 L10,9.5 M14.5,16 L13.5,9.5 M6,16 V19 M17,16 V19 M19,14 C20,12.5 22.5,12.5 22.5,14.5 C22.5,16 21,16.5 19,16 M2.5,15.5 L4,16";
		private const string RabbitIcon = "M3.5,19 C3,14.5 6.5,11.5 10.5,12 C13,12.3 14.5,13.5 15.2,14.8 M15.5,16.5 C15.6,18 14.8,19 13.5,19 H3.5 M15,12.5 A3.2,3.2 0 1,0 21.4,12.5 A3.2,3.2 0 1,0 15,12.5 M16.6,9.8 C15.6,7 15.6,4 16.7,3.2 C17.8,3.6 18.3,6.5 18,9.4 M18.9,9.4 C19.5,6.6 20.7,4.4 21.8,4.7 C22.4,5.5 21.6,8 20.4,10 M3.8,15 C2.3,14.8 2.1,16.8 3.5,17.2 M9.5,19 C9.5,17.5 10.5,16.8 11.8,17 M19.4,12 A0.2,0.2 0 1,0 19.8,12 A0.2,0.2 0 1,0 19.4,12";

		private readonly Button restart = HomeView.IconButton(RestartIcon, L.T("Back to the start of the song"));
		private readonly Button playPause = HomeView.IconButton(PlayIcon, L.T("Scroll the lyrics"));
		// Where the next song would be, after the last song of a setlist.
		private readonly TextBlock noMoreSongs = new() { Text = L.T("No more songs"), FontSize = 14, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), IsVisible = false };
		// The next song as the right drawer shows it (see PageLayout.SongCard), smaller.
		// With the button's room above and below inside it, so the NEXT label beside it fills the button's height.
		private readonly ContentControl nextCard = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4) };
		private readonly Button next;
		// The next button's width, the same for every song (a long name is cut short with "…").
		private const double NextWidth = 232;
		// Whether there's a next song to show, and whether the button fits beside the tools at the right.
		private bool hasNext;
		private bool nextFits = true;
		private readonly LongClickToggleButton bandSync = new()
		{
			Content = new Path
			{
				// The band (as on the left drawer's Band item): band sync.
				Data = Geometry.Parse(AppIcons.Band),
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

		// The MIDI page: the keyboard's sounds, to choose the song's (see MidiSoundsView); only while MIDI is on (see MidiModule).
		private readonly Button midi = HomeView.IconButton(AppIcons.Midi, L.T("MIDI setup"));

		// The media file playing: where it is (drag to move it), the time played and its length.
		// A 4-pixel track filled up to where the sound is, without a handle; pressed or dragged anywhere
		// on it (a taller strip, so it's easy to hit), the sound goes there.
		private const double TrackHeight = 4;
		private readonly Border progressTrack = new() { Height = TrackHeight, CornerRadius = new CornerRadius(TrackHeight / 2), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.35 };
		private readonly Border progressFill = new() { Height = TrackHeight, CornerRadius = new CornerRadius(TrackHeight / 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
		private readonly Panel progress = new() { Height = 20, Background = Brushes.Transparent, Margin = new Thickness(8, 0), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
		// The sound's length (seconds), and where the bar shows it is (0 to 1).
		private double progressSeconds;
		private double progressShare;
		private readonly TextBlock played = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40, TextAlignment = TextAlignment.Right };
		private readonly TextBlock total = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 };
		private readonly DockPanel progressRow = new() { IsVisible = false, Margin = new Thickness(4, 0, 4, 0) };
		private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
		// The media file's speed and pitch, a step at a time: slower and faster (5 %), lower and higher
		// (a semitone); between each pair what it is now, which resets it (as recorded) when clicked.
		private const double SpeedStep = 0.05;
		private readonly Button slower = HomeView.IconButton(TurtleIcon, L.T("Slower"));
		private readonly Button faster = HomeView.IconButton(RabbitIcon, L.T("Faster"));
		private readonly Button lower = SignButton("b", L.T("Lower"));
		private readonly Button higher = SignButton("#", L.T("Higher"));
		private readonly Button speedValue = ValueButton(44);
		private readonly Button pitchValue = ValueButton(30);

		// The media player is on for the song shown: its progress shows also while it doesn't play.
		private bool mediaOn;

		// Set while the bar is pressed (and dragged), or swiped with two fingers.
		private bool draggingProgress;

		// A two-finger swipe (touchpad) over the bar moves it like a drag; the sound goes there once the
		// swipe has settled. One wheel step is as far as dragging this many pixels.
		private const double SwipeStep = 50;
		private readonly DispatcherTimer swipeSettle = new() { Interval = TimeSpan.FromMilliseconds(250) };

		/// <summary>Raised when the user turns band sync on (true) or off (false).</summary>
		public event Action<bool>? BandSyncToggled;

		/// <summary>
		/// Raised on a longclick of the band icon: band sync on, sending this member's setlist and song to
		/// the band (instead of taking the band's). The band icon isn't toggled; call <see cref="SetBandSync"/>.
		/// </summary>
		public event Action? BandSyncSendRequested;

		/// <summary>Raised when the user wants the lyrics back at the start of the song.</summary>
		public event Action? RestartRequested;

		/// <summary>Raised when the user wants the next song in the setlist.</summary>
		public event Action? NextRequested;

		/// <summary>Raised when the user presses play/pause.</summary>
		public event Action? PlayPauseRequested;

		/// <summary>Raised with the MIDI button when it's pressed: the MIDI setup is wanted (in a popup from it).</summary>
		public event Action<Control>? MidiRequested;

		public PlaybackBar()
		{
			// The size of the app's other icon buttons.
			ToolbarSize(restart);
			ToolbarSize(playPause);

			// As tall as the icon buttons (24-pixel icon plus 8 padding above and below).
			next = new Button
			{
				// "NEXT" standing up at the left in the highlight colour, then the next song as in the right
				// drawer (see SetNextSong), smaller.
				Content = NextContent(),
				MinHeight = 40,
				Width = NextWidth,
				// No room at the left: the NEXT label starts at the edge.
				Padding = new Thickness(0, 0, 8, 0),
				HorizontalAlignment = HorizontalAlignment.Left,
				// The song the button's whole width: tempo, beat, key and transpose at its right edge.
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Center,
				// A little darker than the toolbar (a shade of black over it, in both themes), so it
				// stands out as a button.
				Background = new SolidColorBrush(Color.FromArgb(0x28, 0, 0, 0)),
				BorderThickness = new Thickness(0),
			};
			// Pointed at (and pressed): black in the dark theme, white in the light one.
			foreach (var (theme, colour) in new[] { (Avalonia.Styling.ThemeVariant.Dark, Colors.Black), (Avalonia.Styling.ThemeVariant.Light, Colors.White) })
			{
				next.Resources.ThemeDictionaries[theme] = new ResourceDictionary
				{
					["ButtonBackgroundPointerOver"] = new SolidColorBrush(colour),
					["ButtonBackgroundPressed"] = new SolidColorBrush(colour),
				};
			}

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
			// The MIDI setup, just left of the band icon (while MIDI is on, here or on another device).
			DockPanel.SetDock(midi, Dock.Right);
			midi.Margin = new Thickness(4, 0, 0, 0);
			midi.Click += (_, _) => MidiRequested?.Invoke(midi);
			void ShowMidi() => Dispatcher.UIThread.Post(() => midi.IsVisible = MidiModule.Enabled);
			ShowMidi();
			midi.AttachedToVisualTree += (_, _) =>
			{
				MidiModule.EnabledChanged += ShowMidi;
				UserSettings.Changed += ShowMidi;
				ShowMidi();
			};
			midi.DetachedFromVisualTree += (_, _) =>
			{
				MidiModule.EnabledChanged -= ShowMidi;
				UserSettings.Changed -= ShowMidi;
			};
			restart.Margin = new Thickness(4, 0);
			// The next button only where it fits whole (else it's left out: the tools come first).
			var nextSlot = new Panel { Children = { next, noMoreSongs } };
			nextSlot.SizeChanged += (_, e) =>
			{
				nextFits = e.NewSize.Width >= NextWidth;
				ShowNext();
			};
			var row = new DockPanel { Children = { playPause, restart, bandSync, midi, nextSlot } };

			// The media file's progress above the buttons, while one plays: slower and faster at the left,
			// lower and higher at the right, the time played and the length beside the bar.
			slower.Padding = faster.Padding = new Thickness(4);
			foreach (var control in new Control[] { slower, speedValue, faster, played })
			{
				DockPanel.SetDock(control, Dock.Left);
				progressRow.Children.Add(control);
			}

			// Docked from the right edge in: higher, the pitch, lower.
			foreach (var control in new Control[] { higher, pitchValue, lower })
			{
				DockPanel.SetDock(control, Dock.Right);
				progressRow.Children.Add(control);
			}

			DockPanel.SetDock(total, Dock.Right);
			progressRow.Children.Add(total);
			progressRow.Children.Add(progress);
			slower.Click += (_, _) => ChangeSpeed(-1);
			faster.Click += (_, _) => ChangeSpeed(1);
			lower.Click += (_, _) => ChangePitch(-1);
			higher.Click += (_, _) => ChangePitch(1);
			speedValue.Click += (_, _) => ResetSpeed();
			pitchValue.Click += (_, _) => ResetPitch();
			Avalonia.Automation.AutomationProperties.SetName(progress, L.T("Where the sound is"));
			progressTrack.Bind(Border.BackgroundProperty, progressTrack.GetResourceObservable(LyriousTheme.ForegroundBrush));
			progressFill.Bind(Border.BackgroundProperty, progressFill.GetResourceObservable("AccentButtonBackground"));
			progress.Children.Add(progressTrack);
			progress.Children.Add(progressFill);
			progress.SizeChanged += (_, _) => ShowShare();
			// While pressed and dragged, the bar and the time show where it would go; let go, it goes there.
			progress.PointerPressed += (_, e) =>
			{
				draggingProgress = true;
				e.Pointer.Capture(progress);
				DragTo(e);
				e.Handled = true;
			};
			progress.PointerMoved += (_, e) =>
			{
				if (draggingProgress)
				{
					DragTo(e);
				}
			};
			progress.PointerReleased += (_, e) =>
			{
				if (!draggingProgress)
				{
					return;
				}

				DragTo(e);
				draggingProgress = false;
				e.Pointer.Capture(null);
				Seek();
			};
			progress.PointerCaptureLost += (_, _) => draggingProgress = false;
			progress.PointerWheelChanged += (_, e) =>
			{
				// Sideways (two fingers left or right); an up or down swipe or wheel counts too.
				var delta = Math.Abs(e.Delta.X) >= Math.Abs(e.Delta.Y) ? e.Delta.X : -e.Delta.Y;
				var width = progress.Bounds.Width;
				if (delta == 0 || width <= 0 || progressSeconds <= 0)
				{
					return;
				}

				e.Handled = true;
				draggingProgress = true;
				progressShare = Math.Clamp(progressShare + delta * SwipeStep / width, 0, 1);
				ShowShare();
				played.Text = Time(TimeSpan.FromSeconds(progressShare * progressSeconds));
				swipeSettle.Stop();
				swipeSettle.Start();
			};
			swipeSettle.Tick += (_, _) =>
			{
				swipeSettle.Stop();
				draggingProgress = false;
				Seek();
			};
			progressTimer.Tick += (_, _) => ShowProgress();

			var bar = new Border { Padding = new Thickness(4), Child = new StackPanel { Children = { progressRow, row } } };
			bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(LyriousTheme.CardBrush));
			Content = bar;
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			if (App.SoundStore is { } sounds)
			{
				sounds.PlayingChanged += OnSoundChanged;
			}

			ShowProgress();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (App.SoundStore is { } sounds)
			{
				sounds.PlayingChanged -= OnSoundChanged;
			}

			progressTimer.Stop();
			swipeSettle.Stop();
			draggingProgress = false;
		}

		// Raised on any thread.
		private void OnSoundChanged() => Dispatcher.UIThread.Post(ShowProgress);

		/// <summary>
		/// Shows the media file playing in the app, where it is and how long it is; hidden (and not
		/// followed) while none plays. Left alone while its thumb is dragged.
		/// </summary>
		private void ShowProgress()
		{
			var sounds = App.SoundStore;
			var playing = sounds?.Playing != null;
			var duration = playing ? sounds!.Duration : null;
			var position = playing ? sounds!.Position : null;
			// Followed while a sound plays (its length may only be known a moment after it starts).
			progressTimer.IsEnabled = playing && IsAttachedToVisualTree();
			var known = duration is { TotalSeconds: > 0 } && position != null;
			progressRow.IsVisible = known || mediaOn;
			if (!progressRow.IsVisible || draggingProgress)
			{
				return;
			}

			if (!known)
			{
				// On, but not playing (yet): at the start; its length is known once it plays.
				ShowTuning();
				progressSeconds = 0;
				progressShare = 0;
				ShowShare();
				played.Text = Time(TimeSpan.Zero);
				total.Text = "";
				return;
			}

			ShowTuning();
			progressSeconds = duration!.Value.TotalSeconds;
			progressShare = Math.Clamp(position!.Value.TotalSeconds / progressSeconds, 0, 1);
			ShowShare();
			played.Text = Time(position.Value);
			total.Text = Time(duration.Value);
		}

		private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) != null;

		/// <summary>Moves the media file playing to where the bar is.</summary>
		private void Seek()
		{
			App.SoundStore?.Seek(TimeSpan.FromSeconds(progressShare * progressSeconds));
			ShowProgress();
		}

		/// <summary>Shows the media player's progress, speed and pitch for the song (on), also while it doesn't play; or only while a sound plays (off).</summary>
		public void SetMediaOn(bool on)
		{
			mediaOn = on;
			ShowProgress();
		}

		/// <summary>A button with a sign on it, as big as the icon buttons: "#" (sharp, higher) or "b" (flat, lower).</summary>
		private static Button SignButton(string sign, string description)
		{
			var button = new Button
			{
				Content = new TextBlock { Text = sign, FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
				Width = 32,
				Height = 32,
				Padding = new Thickness(0),
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				VerticalAlignment = VerticalAlignment.Center,
			};
			ToolTip.SetTip(button, description);
			Avalonia.Automation.AutomationProperties.SetName(button, description);
			return button;
		}

		/// <summary>The speed or pitch now, between its buttons; clicked, it's set back to as recorded.</summary>
		private static Button ValueButton(double minWidth) => new()
		{
			Content = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
			MinWidth = minWidth,
			Height = 32,
			Padding = new Thickness(2, 0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
		};

		private void ResetSpeed()
		{
			if (App.SoundStore is { CanChangeSpeed: true } sounds)
			{
				sounds.Speed = 1;
				ShowTuning();
			}
		}

		private void ResetPitch()
		{
			if (App.SoundStore is { CanChangePitch: true } sounds)
			{
				sounds.Pitch = 0;
				ShowTuning();
			}
		}

		/// <summary>A step slower (-1) or faster (1), within what the sound files can do.</summary>
		private void ChangeSpeed(int direction)
		{
			if (App.SoundStore is { CanChangeSpeed: true } sounds)
			{
				sounds.Speed = Math.Clamp(Math.Round(sounds.Speed / SpeedStep + direction) * SpeedStep, SoundTuning.MinSpeed, SoundTuning.MaxSpeed);
				ShowTuning();
			}
		}

		/// <summary>A semitone lower (-1) or higher (1), within what the sound files can do.</summary>
		private void ChangePitch(int direction)
		{
			if (App.SoundStore is { CanChangePitch: true } sounds)
			{
				sounds.Pitch = Math.Clamp(Math.Round(sounds.Pitch) + direction, SoundTuning.MinPitch, SoundTuning.MaxPitch);
				ShowTuning();
			}
		}

		/// <summary>The speed and pitch buttons (those the sound files can change), what they are now, and how far they can go.</summary>
		private void ShowTuning()
		{
			var sounds = App.SoundStore;
			var speed = sounds?.Speed ?? 1;
			var pitch = Math.Round(sounds?.Pitch ?? 0);
			slower.IsVisible = faster.IsVisible = speedValue.IsVisible = sounds?.CanChangeSpeed == true;
			lower.IsVisible = higher.IsVisible = pitchValue.IsVisible = sounds?.CanChangePitch == true;
			slower.IsEnabled = speed > SoundTuning.MinSpeed + 0.001;
			faster.IsEnabled = speed < SoundTuning.MaxSpeed - 0.001;
			lower.IsEnabled = pitch > SoundTuning.MinPitch;
			higher.IsEnabled = pitch < SoundTuning.MaxPitch;
			// As recorded: dimmer, and nothing to reset.
			var speedChanged = Math.Abs(speed - 1) > 0.001;
			((TextBlock)speedValue.Content!).Text = SoundTuning.DescribeSpeed(speed);
			((TextBlock)pitchValue.Content!).Text = pitch == 0 ? "0" : pitch.ToString("+0;-0");
			speedValue.Opacity = speedChanged ? 1 : 0.6;
			pitchValue.Opacity = pitch != 0 ? 1 : 0.6;
			Describe(speedValue, speedChanged ? L.T("Speed {0}: click for as recorded", SoundTuning.DescribeSpeed(speed)) : L.T("Speed as recorded"));
			Describe(pitchValue, pitch != 0 ? L.T("Pitch {0}: click for as recorded", SoundTuning.DescribePitch(pitch).ToLowerInvariant()) : L.T("Pitch as recorded"));
			Describe(slower, L.T("Slower (now {0})", SoundTuning.DescribeSpeed(speed)));
			Describe(faster, L.T("Faster (now {0})", SoundTuning.DescribeSpeed(speed)));
			Describe(lower, L.T("A semitone lower (now {0})", SoundTuning.DescribePitch(pitch).ToLowerInvariant()));
			Describe(higher, L.T("A semitone higher (now {0})", SoundTuning.DescribePitch(pitch).ToLowerInvariant()));

			static void Describe(Control control, string text)
			{
				ToolTip.SetTip(control, text);
				Avalonia.Automation.AutomationProperties.SetName(control, text);
			}
		}

		/// <summary>Fills the track up to <see cref="progressShare"/>.</summary>
		private void ShowShare() => progressFill.Width = Math.Max(0, progress.Bounds.Width * progressShare);

		/// <summary>The bar and the time show where the pointer is on it (while it's pressed).</summary>
		private void DragTo(Avalonia.Input.PointerEventArgs e)
		{
			var width = progress.Bounds.Width;
			if (width <= 0 || progressSeconds <= 0)
			{
				return;
			}

			progressShare = Math.Clamp(e.GetPosition(progress).X / width, 0, 1);
			ShowShare();
			played.Text = Time(TimeSpan.FromSeconds(progressShare * progressSeconds));
		}

		/// <summary>"1:05", or "1:02:05" from an hour.</summary>
		private static string Time(TimeSpan time)
			=> time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

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

		/// <summary>The band icon's colour (readable on the highlight when on) and its description.</summary>
		private void ShowBandSync()
		{
			var on = bandSync.IsChecked == true;
			var icon = bandSync.Content is Viewbox { Child: Path inBox } ? inBox : (Path)bandSync.Content!;
			icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable(on ? "ToggleButtonForegroundChecked" : LyriousTheme.ForegroundBrush));
			var description = on
				? L.T("Band sync is on: following and sending the band's song, setlist and play/stop. Tap to turn off; hold to send your setlist and song to the band.")
				: L.T("Band sync is off: not following or sending the band's song, setlist and play/stop. Tap to turn on and take the band's; hold to turn on and send yours.");
			ToolTip.SetTip(bandSync, description);
			Avalonia.Automation.AutomationProperties.SetName(bandSync, on ? L.T("Band sync on") : L.T("Band sync off"));
		}

		/// <summary>Shows play or pause, for whether the lyrics are scrolling.</summary>
		public void SetPlaying(bool playing)
		{
			var icon = playPause.Content is Viewbox { Child: Path inBox } ? inBox : (Path)playPause.Content!;
			icon.Data = Geometry.Parse(playing ? PauseIcon : PlayIcon);
			var description = playing ? L.T("Stop scrolling") : L.T("Scroll the lyrics");
			ToolTip.SetTip(playPause, description);
			Avalonia.Automation.AutomationProperties.SetName(playPause, description);
		}

		/// <summary>The next button's content: the NEXT label, the button's whole height, and the next song beside it.</summary>
		private Control NextContent()
		{
			return new DockPanel { Children = { NextLabel(nextCard), nextCard } };
		}

		/// <summary>
		/// "NEXT", read from the bottom up, bold in the details colour (the text colour, dimmed) on the
		/// highlight colour: what the button is. The button's whole height: the song's two lines with the
		/// room above and below them (<paramref name="song"/>'s margin), the text scaled to fill it.
		/// </summary>
		private static Control NextLabel(Control song)
		{
			var text = new TextBlock
			{
				Text = L.T("Next").ToUpper(System.Globalization.CultureInfo.CurrentCulture),
				FontWeight = FontWeight.Bold,
				Opacity = 0.7,
			};
			text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(LyriousTheme.ForegroundBrush));
			var label = new Border
			{
				Padding = new Thickness(2, 3),
				CornerRadius = new CornerRadius(3),
				Child = new Viewbox
				{
					Stretch = Stretch.Uniform,
					Child = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = text },
				},
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0),
			};
			label.Bind(Border.BackgroundProperty, label.GetResourceObservable("AccentButtonBackground"));
			// From the song (not the button, which grows with the label): a set height, so the text is scaled up to it.
			song.SizeChanged += (_, e) =>
			{
				if (e.HeightChanged && e.NewSize.Height > 0)
				{
					label.Height = e.NewSize.Height + song.Margin.Top + song.Margin.Bottom;
				}
			};
			DockPanel.SetDock(label, Dock.Left);
			return label;
		}

		/// <summary>The next button shows when there's a next song and it fits whole.</summary>
		private void ShowNext() => next.IsVisible = hasNext && nextFits;

		/// <summary>Greys out the next song while playing: the song can't be changed then.</summary>
		public void SetNextAllowed(bool allowed)
		{
			next.IsEnabled = allowed;
		}

		/// <summary>
		/// Shows the next song as the right drawer does: its name, and under it its tags and its tempo,
		/// beat and key (the key moved by the member's own transpose, with its value beside it). After the last song of a setlist
		/// "No more songs" is shown there instead; for a song shown from the songbook, nothing.
		/// </summary>
		/// <param name="nextSong">The next song; none, nothing (or "No more songs").</param>
		/// <param name="lastInSetlist">The current song is the setlist's last: "No more songs" is shown instead.</param>
		public void SetNextSong(SongDetails? nextSong, bool lastInSetlist = false)
		{
			var nextSongName = nextSong?.Name;
			hasNext = nextSong != null;
			ShowNext();
			noMoreSongs.IsVisible = nextSong == null && lastInSetlist;
			nextCard.Content = nextSong == null ? null : PageLayout.SongCard(nextSong, 14, 11, 9, tagsOnOneLine: true);
			// No tooltip (the button shows it all); its name for screen readers.
			var description = nextSongName == null ? L.T("Next song") : L.T("Next song: {0}", nextSongName);
			Avalonia.Automation.AutomationProperties.SetName(next, description);
		}
	}
}
