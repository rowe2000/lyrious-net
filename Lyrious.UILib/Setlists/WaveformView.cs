using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's sound file as a waveform: where the song starts and stops in it (dragged, or set where it
	/// plays or was clicked), and its tempo, shown as bar lines over the waveform from the start (bar 1
	/// there), which can be found from the sound (<see cref="TempoFinder"/>) and nudged until the lines
	/// sit on the beats. It plays from the start (or where clicked) to the stop, to listen. Save gives
	/// the start, stop and tempo (see <see cref="SaveRequested"/>); ✕ leaves it as it was.
	/// </summary>
	public sealed class WaveformView : UserControl
	{
		private readonly SoundFile sound;
		private readonly ISoundStore sounds;
		private readonly int beatsPerBar;
		private readonly WaveCanvas canvas;
		private readonly ScrollViewer scroller;
		private readonly NumericUpDown tempoBox;
		private readonly TextBlock info = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
		private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
		private readonly Button play = HomeView.IconButton(PlayIcon, L.T("Play from the start (or where you clicked) to the stop"));
		private readonly Button findTempo = new() { Content = L.T("Find tempo"), IsEnabled = false };
		private readonly Button save = new() { Content = L.T("Save"), IsEnabled = false };
		private readonly DispatcherTimer playWatch = new() { Interval = TimeSpan.FromMilliseconds(30) };

		// Whether the sound was started here (play): only then it's stopped when the view closes. One
		// that played when it opened (the song playing) plays on.
		private bool startedHere;

		private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
		private const string StopIcon = "M7,7 H17 V17 H7 Z";
		private const string ZoomInIcon = "M4,10.5 A6.5,6.5 0 1 0 17,10.5 A6.5,6.5 0 1 0 4,10.5 M15.2,15.2 L20,20 M7.5,10.5 H13.5 M10.5,7.5 V13.5";
		private const string ZoomOutIcon = "M4,10.5 A6.5,6.5 0 1 0 17,10.5 A6.5,6.5 0 1 0 4,10.5 M15.2,15.2 L20,20 M7.5,10.5 H13.5";

		/// <summary>Saves the sound with its start and stop (in its link) and the song's tempo; throws when it can't.</summary>
		public Func<SoundFile, double, Task>? SaveRequested { get; set; }

		/// <summary>Raised when ✕ is pressed, or after saving: whoever showed it shows the song again.</summary>
		public event Action? Closed;

		/// <param name="tempo">The song's tempo now (beats per minute).</param>
		/// <param name="beatsPerBar">The song's beats per bar (the bars' lines are drawn by it).</param>
		public WaveformView(SoundFile sound, ISoundStore sounds, double tempo, int beatsPerBar, string songName)
		{
			this.sound = sound;
			this.sounds = sounds;
			this.beatsPerBar = Math.Max(1, beatsPerBar);
			canvas = new WaveCanvas { BeatsPerBar = this.beatsPerBar, Tempo = tempo > 0 ? tempo : 120, Start = sound.Start ?? 0, Stop = sound.Stop };
			scroller = new ScrollViewer
			{
				Content = canvas,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
				Height = 200,
			};
			scroller.SizeChanged += (_, _) => canvas.MinWidth = scroller.Viewport.Width;
			// The wheel (up and down, or a touchpad's sideways swipe) scrolls the waveform sideways; with
			// Ctrl (⌘ on a Mac) it zooms in or out around the spot under the pointer.
			scroller.AddHandler(PointerWheelChangedEvent, (_, e) =>
			{
				if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
				{
					if (e.Delta.Y != 0)
					{
						Zoom(Math.Pow(WheelZoom, e.Delta.Y), e.GetPosition(scroller).X);
						e.Handled = true;
					}

					return;
				}

				var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
				if (delta != 0)
				{
					ScrollTo(scroller.Offset.X - delta * WheelStep);
					e.Handled = true;
				}
			}, Avalonia.Interactivity.RoutingStrategies.Tunnel);
			// A drag on the waveform (mouse or finger, not on the start or stop line) moves it along.
			var dragFrom = 0.0;
			canvas.DragStarted += () => dragFrom = scroller.Offset.X;
			canvas.Dragged += moved => ScrollTo(dragFrom - moved);
			message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(LyriousTheme.ErrorBrush));

			tempoBox = new NumericUpDown
			{
				Minimum = 30,
				Maximum = 300,
				Increment = 0.1m,
				FormatString = "0.0",
				Value = (decimal)Math.Round(canvas.Tempo, 1),
				Width = 130,
			};
			ToolTip.SetTip(tempoBox, L.T("The song's tempo (beats per minute): change it until the bar lines sit on the beats"));
			tempoBox.ValueChanged += (_, _) =>
			{
				if (tempoBox.Value is { } value)
				{
					canvas.Tempo = (double)value;
					Changed();
				}
			};

			var setStart = new Button { Content = L.T("Set start") };
			ToolTip.SetTip(setStart, L.T("The song starts here (bar 1): where it plays now, or where you clicked"));
			setStart.Click += (_, _) =>
			{
				canvas.Start = Math.Min(canvas.Here, canvas.Stop ?? double.MaxValue);
				Changed();
			};
			var setStop = new Button { Content = L.T("Set stop") };
			ToolTip.SetTip(setStop, L.T("The song stops here: where it plays now, or where you clicked"));
			setStop.Click += (_, _) =>
			{
				canvas.Stop = Math.Max(canvas.Here, canvas.Start);
				Changed();
			};
			ToolTip.SetTip(findTempo, L.T("Finds the tempo between the start and the stop, and puts the start on the nearest beat"));
			findTempo.Click += (_, _) => FindTempo();
			play.Click += (_, _) => _ = PlayOrStopAsync();
			var zoomIn = HomeView.IconButton(ZoomInIcon, L.T("Zoom in"));
			zoomIn.Click += (_, _) => Zoom(1.6);
			var zoomOut = HomeView.IconButton(ZoomOutIcon, L.T("Zoom out"));
			zoomOut.Click += (_, _) => Zoom(1 / 1.6);
			save.Classes.Add("accent");
			save.Click += async (_, _) => await SaveAsync();

			canvas.Changed += Changed;
			canvas.Clicked += seconds =>
			{
				// Playing: it goes there.
				if (sounds.Playing == sound.Id)
				{
					sounds.Seek(TimeSpan.FromSeconds(seconds));
				}

				ShowInfo();
			};
			playWatch.Tick += (_, _) => FollowPlaying();

			var tools = new WrapPanel
			{
				Orientation = Orientation.Horizontal,
				ItemSpacing = 8,
				LineSpacing = 8,
				Children =
				{
					play,
					setStart,
					setStop,
					findTempo,
					new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = L.T("Tempo"), VerticalAlignment = VerticalAlignment.Center }, tempoBox } },
					zoomOut,
					zoomIn,
					save,
				},
			};

			var column = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					tools,
					info,
					message,
					scroller,
					new TextBlock
					{
						Text = L.T("Drag the green line to where the song starts (bar 1) and the red one to where it stops, or play and press Set start and Set stop. Click to play from there. The lines over the waveform are the bars at the song's tempo: find it, or change it until they sit on the beats."),
						Opacity = 0.7,
						FontSize = 13,
						TextWrapping = TextWrapping.Wrap,
					},
				},
			};

			Content = PageLayout.Page(sound.Name, new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				subtitle: L.T("{0} · start, stop and tempo", songName), closable: true, icon: AppIcons.SoundFiles);
			// Its ✕: back to the song, nothing changed.
			AddHandler(PageLayout.CloseRequestedEvent, (_, e) =>
			{
				e.Handled = true;
				StopIfStartedHere();
				Closed?.Invoke();
			});
			DetachedFromVisualTree += (_, _) => StopIfStartedHere();

			info.Text = L.T("Reading the sound…");
			_ = LoadAsync();
		}

		/// <summary>Reads the sound's waveform (fetching it from the server first if it isn't on this device).</summary>
		private async Task LoadAsync()
		{
			try
			{
				var wave = await sounds.ReadWaveAsync(sound.Id);
				if (wave == null && await SoundSync.FetchAsync(sound.Id))
				{
					wave = await sounds.ReadWaveAsync(sound.Id);
				}

				if (wave == null || wave.Levels.Length == 0)
				{
					Show(L.T("The sound can't be read here, so its waveform can't be shown."));
					info.Text = "";
					return;
				}

				canvas.Wave = wave;
				canvas.Stop = canvas.Stop is { } stop ? Math.Min(stop, wave.Seconds) : null;
				findTempo.IsEnabled = true;
				ShowInfo();
				// Opened while it plays: it plays on, the playhead following it (and Stop to stop it).
				if (sounds.Playing == sound.Id)
				{
					SetPlayIcon(playing: true);
					playWatch.Start();
				}
			}
			catch (Exception ex)
			{
				Show(L.T("Couldn't read the sound. {0}", ex.Message));
				info.Text = "";
			}
		}

		/// <summary>Something was changed (start, stop or tempo): it can be saved, and the line under the tools says what it is now.</summary>
		private void Changed()
		{
			save.IsEnabled = canvas.Wave != null;
			ShowInfo();
		}

		/// <summary>The start, the stop, the tempo and the bars between them, in words.</summary>
		private void ShowInfo()
		{
			if (canvas.Wave is not { } wave)
			{
				return;
			}

			var stop = canvas.Stop ?? wave.Seconds;
			var bars = (stop - canvas.Start) * canvas.Tempo / 60 / beatsPerBar;
			info.Text = L.T("Start {0} · stop {1} · {2} bpm, {3} beats a bar · about {4} bars", Time(canvas.Start), canvas.Stop is { } s ? Time(s) : L.T("the end"),
				canvas.Tempo.ToString("0.0", CultureInfo.CurrentCulture), beatsPerBar, Math.Max(0, Math.Round(bars)));
		}

		/// <summary>"1:05.25": minutes, seconds and hundredths.</summary>
		private static string Time(double seconds)
		{
			// Rounded to the hundredth (1.2999 is 0:01.30), then split.
			var hundredths = (long)Math.Round(Math.Max(0, seconds) * 100);
			return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
		}

		/// <summary>Finds the tempo between the start and the stop, and puts the start on the nearest beat.</summary>
		private void FindTempo()
		{
			if (canvas.Wave is not { } wave)
			{
				return;
			}

			message.IsVisible = false;
			var stop = canvas.Stop ?? wave.Seconds;
			if (TempoFinder.Find(wave, canvas.Start, stop, canvas.Start) is not { } found)
			{
				Show(L.T("No tempo could be found: the stretch between the start and the stop must be at least 4 seconds, with beats to hear."));
				return;
			}

			canvas.Start = found.Beat;
			tempoBox.Value = (decimal)Math.Round(found.Tempo, 1);
			canvas.Tempo = (double)tempoBox.Value;
			Changed();
		}

		// How far one notch of the wheel scrolls the waveform (pixels), and how much one zooms it (with Ctrl).
		private const double WheelStep = 60;
		private const double WheelZoom = 1.25;

		/// <summary>Scrolls the waveform so <paramref name="x"/> (pixels from its start) is at the left, as far as it goes.</summary>
		private void ScrollTo(double x)
			=> scroller.Offset = new Vector(Math.Clamp(x, 0, Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width)), 0);

		/// <summary>
		/// Zooms in (more than 1) or out, keeping the moment at <paramref name="at"/> (pixels from the view's
		/// left; its middle when not given) where it is.
		/// </summary>
		private void Zoom(double factor, double? at = null)
		{
			var anchor = at ?? scroller.Viewport.Width / 2;
			var seconds = (scroller.Offset.X + anchor) / canvas.PixelsPerSecond;
			canvas.PixelsPerSecond = Math.Clamp(canvas.PixelsPerSecond * factor, 5, 800);
			scroller.UpdateLayout();
			ScrollTo(seconds * canvas.PixelsPerSecond - anchor);
		}

		/// <summary>Plays from the start (or where clicked) to the stop; or stops it playing.</summary>
		private async Task PlayOrStopAsync()
		{
			if (sounds.Playing == sound.Id)
			{
				StopPlaying();
				return;
			}

			message.IsVisible = false;
			if (!await sounds.PlayAsync(sound.Id) && !(await SoundSync.FetchAsync(sound.Id) && await sounds.PlayAsync(sound.Id)))
			{
				Show(L.T("The sound isn't on this device, nor on the server."));
				return;
			}

			startedHere = true;
			sounds.Seek(TimeSpan.FromSeconds(canvas.ClickedAt ?? canvas.Start));
			SetPlayIcon(playing: true);
			playWatch.Start();
		}

		/// <summary>While it plays: the playhead where it is (kept in sight), and stopping at the stop.</summary>
		private void FollowPlaying()
		{
			if (sounds.Playing != sound.Id || sounds.Position is not { } position)
			{
				if (sounds.Playing != sound.Id)
				{
					StopPlaying();
				}

				return;
			}

			var seconds = position.TotalSeconds;
			if (canvas.Stop is { } stop && seconds >= stop)
			{
				StopPlaying();
				return;
			}

			canvas.Playhead = seconds;
			var x = seconds * canvas.PixelsPerSecond;
			if (x < scroller.Offset.X || x > scroller.Offset.X + scroller.Viewport.Width - 40)
			{
				scroller.Offset = new Vector(Math.Max(0, x - 40), 0);
			}
		}

		/// <summary>Leaving: stops the sound if it was started here (one that played already plays on), and stops following it.</summary>
		private void StopIfStartedHere()
		{
			if (startedHere)
			{
				StopPlaying();
			}
			else
			{
				playWatch.Stop();
			}
		}

		/// <summary>Stops the sound if it plays here.</summary>
		private void StopPlaying()
		{
			startedHere = false;
			playWatch.Stop();
			if (sounds.Playing == sound.Id)
			{
				sounds.Stop();
			}

			canvas.Playhead = null;
			SetPlayIcon(playing: false);
		}

		private void SetPlayIcon(bool playing)
		{
			if (play.Content is Avalonia.Controls.Shapes.Path icon)
			{
				icon.Data = Geometry.Parse(playing ? StopIcon : PlayIcon);
			}

			ToolTip.SetTip(play, playing ? L.T("Stop") : L.T("Play from the start (or where you clicked) to the stop"));
		}

		private async Task SaveAsync()
		{
			if (SaveRequested == null)
			{
				return;
			}

			save.IsEnabled = false;
			message.IsVisible = false;
			try
			{
				StopIfStartedHere();
				var stop = canvas.Wave is { } wave && canvas.Stop is { } s && s >= wave.Seconds - 0.05 ? null : canvas.Stop;
				await SaveRequested(sound with { Start = canvas.Start > 0.005 ? canvas.Start : null, Stop = stop }, canvas.Tempo);
				Closed?.Invoke();
			}
			catch (Exception ex)
			{
				Show(L.T("Couldn't save. {0}", ex.Message));
				save.IsEnabled = true;
			}
		}

		private void Show(string text)
		{
			message.Text = text;
			message.IsVisible = true;
		}

		/// <summary>
		/// The waveform itself, as wide as the sound at <see cref="PixelsPerSecond"/>: the levels, the bar and
		/// beat lines from the start at the tempo, the start (green) and stop (red) lines to drag, where it
		/// was clicked, and the playhead.
		/// </summary>
		private sealed class WaveCanvas : Control
		{
			private SoundWave? wave;
			private double pixelsPerSecond = 40;
			private double tempo = 120;
			private double start;
			private double? stop;
			private double? playhead;
			private double? clicked;
			// Which line is being dragged: the start (-1), the stop (1), or none.
			private int dragging;

			// A press on the waveform (not a line): where it was (in the view, which doesn't move as the
			// waveform scrolls), and whether it has moved far enough to drag the waveform along.
			private Point? pressedAt;
			private bool panning;

			// How far a press moves before it's a drag, not a click.
			private const double DragDistance = 4;

			// How near (in pixels) a press must be to a line to drag it.
			private const double Grab = 8;

			public SoundWave? Wave
			{
				get => wave;
				set
				{
					wave = value;
					InvalidateMeasure();
					InvalidateVisual();
				}
			}

			public double PixelsPerSecond
			{
				get => pixelsPerSecond;
				set
				{
					pixelsPerSecond = value;
					InvalidateMeasure();
					InvalidateVisual();
				}
			}

			public double Tempo
			{
				get => tempo;
				set
				{
					tempo = Math.Max(1, value);
					InvalidateVisual();
				}
			}

			public int BeatsPerBar { get; init; } = 4;

			/// <summary>Where the song starts (bar 1), in seconds.</summary>
			public double Start
			{
				get => start;
				set
				{
					start = Math.Max(0, value);
					InvalidateVisual();
				}
			}

			/// <summary>Where the song stops, in seconds; null at the sound's end.</summary>
			public double? Stop
			{
				get => stop;
				set
				{
					stop = value;
					InvalidateVisual();
				}
			}

			/// <summary>Where the sound plays now; null when it doesn't.</summary>
			public double? Playhead
			{
				get => playhead;
				set
				{
					playhead = value;
					InvalidateVisual();
				}
			}

			/// <summary>Where it was last clicked; null before.</summary>
			public double? ClickedAt => clicked;

			/// <summary>Where a start or stop set now goes: where it plays, else where it was clicked, else the start.</summary>
			public double Here => playhead ?? clicked ?? start;

			/// <summary>Raised when the start or the stop is dragged.</summary>
			public event Action? Changed;

			/// <summary>Raised with the time clicked (not on a line): pressed and let go without moving.</summary>
			public event Action<double>? Clicked;

			/// <summary>Raised when a press on the waveform starts dragging it.</summary>
			public event Action? DragStarted;

			/// <summary>Raised with how far (pixels, right is more) the drag has moved from where it started.</summary>
			public event Action<double>? Dragged;

			/// <summary>Where the pointer is in the view around the waveform (still, as the waveform scrolls in it).</summary>
			private Point InView(PointerEventArgs e) => e.GetPosition(Parent as Visual ?? this);

			public WaveCanvas()
			{
				ClipToBounds = true;
				Cursor = new Cursor(StandardCursorType.Arrow);
				ActualThemeVariantChanged += (_, _) => InvalidateVisual();
			}

			protected override Size MeasureOverride(Size availableSize)
				=> new((wave?.Seconds ?? 0) * pixelsPerSecond, double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

			private double X(double seconds) => seconds * pixelsPerSecond;

			private double SecondsAt(double x) => Math.Clamp(x / pixelsPerSecond, 0, wave?.Seconds ?? 0);

			private IBrush Brush(string key, IBrush fallback)
				=> this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

			public override void Render(DrawingContext context)
			{
				var height = Bounds.Height;
				var middle = height / 2;
				var text = Brush(LyriousTheme.ForegroundBrush, Brushes.White);
				var accent = Brush("AccentButtonBackground", Brushes.MediumPurple);
				var green = Brush(LyriousTheme.SuccessBrush, Brushes.LimeGreen);
				var red = Brush(LyriousTheme.ErrorBrush, Brushes.IndianRed);
				context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
				if (wave is not { } shown)
				{
					return;
				}

				var end = stop ?? shown.Seconds;
				// The levels: each pixel's loudest slice, mirrored above and below the middle; brighter in the song.
				var inSong = new Pen(text, 1);
				var outside = new Pen(text, 1);
				outside.Brush = new SolidColorBrush(((ISolidColorBrush)text).Color, 0.25);
				inSong.Brush = new SolidColorBrush(((ISolidColorBrush)text).Color, 0.7);
				var perPixel = shown.Rate / pixelsPerSecond;
				var width = (int)Math.Ceiling(X(shown.Seconds));
				for (var x = 0; x < width; x++)
				{
					var from = (int)(x * perPixel);
					var to = Math.Min(shown.Levels.Length, Math.Max(from + 1, (int)((x + 1) * perPixel)));
					var level = 0f;
					for (var i = from; i < to; i++)
					{
						level = Math.Max(level, shown.Levels[i]);
					}

					var half = Math.Max(0.5, level * (middle - 14));
					var seconds = x / pixelsPerSecond;
					context.DrawLine(seconds >= start && seconds <= end ? inSong : outside, new Point(x + 0.5, middle - half), new Point(x + 0.5, middle + half));
				}

				// The beats from the start, every bar's first stronger and numbered.
				var beat = 60 / tempo;
				var beatPen = new Pen(new SolidColorBrush(((ISolidColorBrush)text).Color, 0.18), 1);
				var barPen = new Pen(accent, 1.5);
				if (beat * pixelsPerSecond >= 3)
				{
					var number = 0;
					for (var at = start; at <= end + 1e-6; at += beat, number++)
					{
						var x = Math.Round(X(at)) + 0.5;
						var isBar = number % BeatsPerBar == 0;
						context.DrawLine(isBar ? barPen : beatPen, new Point(x, 14), new Point(x, height));
						if (isBar && beat * BeatsPerBar * pixelsPerSecond >= 18)
						{
							var label = new FormattedText((number / BeatsPerBar + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
								FlowDirection.LeftToRight, Typeface.Default, 11, accent);
							context.DrawText(label, new Point(x + 2, 0));
						}
					}
				}

				// Where it was clicked, the playhead, and the start and stop lines with their handles.
				if (clicked is { } at2 && playhead == null)
				{
					var x = Math.Round(X(at2)) + 0.5;
					context.DrawLine(new Pen(text, 1, new DashStyle([3, 3], 0)), new Point(x, 14), new Point(x, height));
				}

				if (playhead is { } now)
				{
					var x = Math.Round(X(now)) + 0.5;
					context.DrawLine(new Pen(text, 2), new Point(x, 0), new Point(x, height));
				}

				Marker(context, X(start), green);
				Marker(context, X(end), red);
			}

			/// <summary>A start or stop line, with a handle at the top to drag it by.</summary>
			private static void Marker(DrawingContext context, double x, IBrush brush)
			{
				var line = Math.Round(x) + 0.5;
				context.DrawLine(new Pen(brush, 2), new Point(line, 0), new Point(line, 10000));
				var handle = new StreamGeometry();
				using (var g = handle.Open())
				{
					g.BeginFigure(new Point(line - 6, 0), true);
					g.LineTo(new Point(line + 6, 0));
					g.LineTo(new Point(line, 9));
					g.EndFigure(true);
				}

				context.DrawGeometry(brush, null, handle);
			}

			protected override void OnPointerPressed(PointerPressedEventArgs e)
			{
				base.OnPointerPressed(e);
				// The left button, or a finger or pen.
				if (wave == null || (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed))
				{
					return;
				}

				var x = e.GetPosition(this).X;
				var toStart = Math.Abs(x - X(start));
				var toStop = Math.Abs(x - X(stop ?? wave.Seconds));
				dragging = toStart <= Grab && toStart <= toStop ? -1 : toStop <= Grab ? 1 : 0;
				// The line, or the waveform (a click or a drag, as it turns out).
				e.Pointer.Capture(this);
				if (dragging == 0)
				{
					pressedAt = InView(e);
					panning = false;
				}

				e.Handled = true;
			}

			protected override void OnPointerMoved(PointerEventArgs e)
			{
				base.OnPointerMoved(e);
				if (wave == null)
				{
					return;
				}

				var x = e.GetPosition(this).X;
				if (pressedAt is { } from)
				{
					var moved = InView(e).X - from.X;
					if (!panning && Math.Abs(moved) >= DragDistance)
					{
						panning = true;
						Cursor = new Cursor(StandardCursorType.SizeWestEast);
						DragStarted?.Invoke();
					}

					if (panning)
					{
						Dragged?.Invoke(moved);
					}

					e.Handled = true;
					return;
				}

				if (dragging == 0)
				{
					// A hand over a line that can be dragged.
					var near = Math.Abs(x - X(start)) <= Grab || Math.Abs(x - X(stop ?? wave.Seconds)) <= Grab;
					Cursor = new Cursor(near ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow);
					return;
				}

				var seconds = SecondsAt(x);
				if (dragging < 0)
				{
					start = Math.Min(seconds, stop ?? wave.Seconds);
				}
				else
				{
					stop = Math.Max(seconds, start);
				}

				InvalidateVisual();
				Changed?.Invoke();
				e.Handled = true;
			}

			protected override void OnPointerReleased(PointerReleasedEventArgs e)
			{
				base.OnPointerReleased(e);
				if (pressedAt != null)
				{
					// Let go without moving: a click, where it was pressed.
					if (!panning && wave != null)
					{
						clicked = SecondsAt(e.GetPosition(this).X);
						InvalidateVisual();
						Clicked?.Invoke(clicked.Value);
					}

					pressedAt = null;
					panning = false;
					Cursor = new Cursor(StandardCursorType.Arrow);
					e.Pointer.Capture(null);
					e.Handled = true;
					return;
				}

				if (dragging != 0)
				{
					dragging = 0;
					e.Pointer.Capture(null);
					e.Handled = true;
				}
			}

			protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
			{
				base.OnPointerCaptureLost(e);
				pressedAt = null;
				panning = false;
				dragging = 0;
			}
		}
	}
}
