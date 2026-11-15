using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's lyrics in large type, scrollable, with its key, tempo and beat. The pencil button
	/// switches to editing the lyrics in place; Save (✓) and Cancel (✕) then take its place beside
	/// the title: Save stores the lyrics, Cancel drops the changes.
	/// Tapping the lyrics once asks for focus mode on or off (<see cref="FocusModeToggled"/>); in
	/// focus mode only the song's name, key, tempo, beat and lyrics are shown. The lyrics can also
	/// scroll by themselves (<see cref="SetAutoScroll"/>), at <see cref="ScrollSpeed"/>.
	/// </summary>
	public sealed class LyricScrollView : UserControl
	{
		// A pencil: the body, its tip, and the line where the tip meets the body.
		private const string EditIcon = "M4,20 L8,19 L19,8 L16,5 L5,16 Z M14,7 L17,10";
		private const string SaveIcon = "M5,12 L10,17 L19,7";
		private const string CancelIcon = "M6,6 L18,18 M18,6 L6,18";

		/// <summary>The most the server stores for a song's lyrics.</summary>
		private const int MaxLyricsLength = 5000;

		private readonly ContentControl body = new();
		private readonly Button editButton;
		// At the right of the title: the pencil, or Save and Cancel while editing. All three are icon
		// buttons of the same size and this area has a fixed size, so the title doesn't move or
		// rewrap when editing starts or stops. (40 = a 24-pixel icon plus 8 padding on each side.)
		private readonly ContentControl titleActions = new()
		{
			Width = 84,
			Height = 40,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalContentAlignment = HorizontalAlignment.Right,
		};
		private readonly Func<string, Task<SongDetails>> saveLyrics;
		private readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
		private readonly System.Diagnostics.Stopwatch scrollClock = new();
		private ScrollViewer? scroller;
		private double scrollRemainder;

		// Timed scrolling: the lyrics' time tags, and from them a timeline of (seconds, content y)
		// that should be at the middle of the view at that time. Rebuilt when the layout changes.
		private IReadOnlyList<TimeMarker> markers = [];
		private Control? lyricsContent;
		private List<(double Seconds, double Y)>? timeline;
		private Size timelineFor;
		private double playbackStart;

		// While timed lyrics play, the lines outside the current part (from the last time tag passed to
		// the next) are a little dimmer. The lyrics' lines (a chorus's lines one by one), and where each
		// part starts: (seconds, first line), in time order.
		private const double DimmedOpacity = 0.7;
		private List<Control> lines = [];
		private List<double> lineOpacities = [];
		private List<(double Seconds, int Line)> parts = [];
		private int currentPart = -1;

		/// <summary>
		/// Whether the song's time tags set the scrolling (instead of <see cref="ScrollSpeed"/>): each
		/// tagged place reaches the middle of the view at its time.
		/// </summary>
		public bool IsTimed => markers.Count > 0;

		/// <summary>How fast the lyrics scroll by themselves, in pixels per second (when not <see cref="IsTimed"/>).</summary>
		public double ScrollSpeed { get; set; } = 12;

		/// <summary>The song shown.</summary>
		public Guid SongId => song.Id;

		/// <summary>Whether the lyrics editor is open (so the page shouldn't be replaced under the user).</summary>
		public bool IsEditing => titleActions.Content != editButton;

		/// <summary>Whether the lyrics are scrolling by themselves.</summary>
		public bool IsAutoScrolling => scrollTimer.IsEnabled;

		/// <summary>Raised when scrolling by itself starts or stops. Reaching the end doesn't stop it.</summary>
		public event Action? AutoScrollChanged;
		private SongDetails song;

		/// <summary>Raised when the user taps the lyrics, to turn focus mode on or off.</summary>
		public event Action? FocusModeToggled;

		/// <param name="saveLyrics">Stores new lyrics for the song and returns the updated song; throws if that fails.</param>
		/// <param name="startEditing">Opens with the lyrics editor showing, e.g. for a new empty song.</param>
		public LyricScrollView(SongDetails song, Func<string, Task<SongDetails>> saveLyrics, bool startEditing = false)
		{
			this.song = song;
			this.saveLyrics = saveLyrics;

			editButton = HomeView.IconButton(EditIcon, "Edit lyrics");
			editButton.Click += (_, _) => ShowEditor();
			scrollTimer.Tick += (_, _) => ScrollStep();
			nudgeTimer.Tick += (_, _) => NudgeStep();

			if (startEditing)
			{
				ShowEditor();
			}
			else
			{
				ShowLyrics();
			}

			Content = PageLayout.Page(
				song.Name,
				body,
				subtitle: PageLayout.SongInfo(song),
				titleAction: titleActions);
		}

		/// <summary>
		/// Shows or hides everything but the song's name, details and lyrics (that is, the pencil).
		/// The pencil is made invisible rather than removed, so the title and lyrics don't move.
		/// </summary>
		public void SetFocusMode(bool focused)
		{
			editButton.Opacity = focused ? 0 : 1;
			editButton.IsHitTestVisible = !focused;
		}

		/// <summary>Scrolls the lyrics back to the start; if they're scrolling by themselves, they go on from there.</summary>
		public void ScrollToStart()
		{
			if (scroller != null)
			{
				StopNudge();
				scroller.Offset = new Vector(scroller.Offset.X, 0);
				scrollRemainder = 0;
				if (IsTimed && scrollTimer.IsEnabled)
				{
					// Back to the start of the song's timeline.
					playbackStart = 0;
					scrollClock.Restart();
				}
			}
		}

		/// <summary>Starts or stops scrolling the lyrics by themselves (not while editing).</summary>
		public void SetAutoScroll(bool on)
		{
			if (on && scroller == null)
			{
				on = false;
			}

			if (on == scrollTimer.IsEnabled)
			{
				return;
			}

			StopNudge();

			if (on)
			{
				// Starting at the end begins again from the top.
				if (scroller!.Offset.Y >= MaxOffset(scroller) - 1)
				{
					scroller.Offset = new Vector(scroller.Offset.X, 0);
				}

				scrollRemainder = 0;
				// Timed: pick up the song's timeline where the lyrics are now (0:00 at the top).
				playbackStart = IsTimed && scroller.Offset.Y > 0 && Timeline() is { } points
					? TimeAt(points, scroller.Offset.Y + scroller.Viewport.Height / 2)
					: 0;
				scrollClock.Restart();
				scrollTimer.Start();
			}
			else
			{
				scrollTimer.Stop();
				scrollClock.Reset();
				ShowCurrentPart(null);
			}

			AutoScrollChanged?.Invoke();
		}

		/// <summary>Moves the lyrics on by the time since the last step, so the speed doesn't depend on the frame rate.</summary>
		private void ScrollStep()
		{
			if (scroller == null)
			{
				SetAutoScroll(false);
				return;
			}

			if (scroller.Extent.Height <= 0)
			{
				return; // Not laid out yet (just opened); the end isn't known.
			}

			if (IsTimed && Timeline() is { } points)
			{
				// Where the song's timeline says the lyrics should be now (not a step from where they are).
				var now = playbackStart + scrollClock.Elapsed.TotalSeconds;
				ShowCurrentPart(now);
				var target = Math.Clamp(CenterAt(points, now) - scroller.Viewport.Height / 2, 0, MaxOffset(scroller));
				scroller.Offset = new Vector(scroller.Offset.X, Math.Round(target));
				return;
			}

			var seconds = scrollClock.Elapsed.TotalSeconds;
			scrollClock.Restart();

			// Offsets snap to whole pixels on some platforms, so the fractions are carried over.
			scrollRemainder += ScrollSpeed * seconds;
			var step = Math.Floor(scrollRemainder);
			if (step < 1)
			{
				return;
			}

			scrollRemainder -= step;
			// At the end it simply stays there, still playing, until the user stops it.
			var y = Math.Min(scroller.Offset.Y + step, MaxOffset(scroller));
			scroller.Offset = new Vector(scroller.Offset.X, y);
		}

		/// <summary>
		/// The timeline from the time tags: (seconds, the content's y at the tag), in time order, with y
		/// never going back up. It starts at 0:00 with the top of the lyrics in view. Built after layout,
		/// and again when the view's size changes (lines wrap differently). Null before layout.
		/// </summary>
		private List<(double Seconds, double Y)>? Timeline()
		{
			if (scroller == null || lyricsContent == null || scroller.Extent.Height <= 0)
			{
				return null;
			}

			if (timeline != null && timelineFor == scroller.Viewport)
			{
				return timeline;
			}

			var points = new List<(double Seconds, double Y)>();
			foreach (var marker in markers.OrderBy(m => m.Time))
			{
				if (marker.Anchor.TranslatePoint(new Point(0, marker.Anchor.Bounds.Height / 2), lyricsContent) is { } place
				    && (points.Count == 0 || (marker.Time.TotalSeconds > points[^1].Seconds && place.Y >= points[^1].Y)))
				{
					points.Add((marker.Time.TotalSeconds, place.Y));
				}
			}

			if (points.Count == 0)
			{
				return null;
			}

			// At 0:00 the lyrics start at the top: the middle of the view shows the content's y = half its height.
			if (points[0].Seconds > 0)
			{
				points.Insert(0, (0, Math.Min(scroller.Viewport.Height / 2, points[0].Y)));
			}

			timeline = points;
			timelineFor = scroller.Viewport;
			return points;
		}

		/// <summary>The content y that should be in the middle of the view at a time; after the last tag, at the last section's pace.</summary>
		private static double CenterAt(List<(double Seconds, double Y)> points, double seconds)
		{
			if (points.Count == 1 || seconds <= points[0].Seconds)
			{
				return points[0].Y;
			}

			for (var i = 1; i < points.Count; i++)
			{
				if (seconds <= points[i].Seconds)
				{
					var (t0, y0) = points[i - 1];
					var (t1, y1) = points[i];
					return y0 + (y1 - y0) * (seconds - t0) / (t1 - t0);
				}
			}

			var (ta, ya) = points[^2];
			var (tb, yb) = points[^1];
			return yb + (yb - ya) / (tb - ta) * (seconds - tb);
		}

		/// <summary>The time at which a content y is in the middle of the view (the reverse of <see cref="CenterAt"/>).</summary>
		private static double TimeAt(List<(double Seconds, double Y)> points, double y)
		{
			if (points.Count == 1 || y <= points[0].Y)
			{
				return points[0].Seconds;
			}

			for (var i = 1; i < points.Count; i++)
			{
				if (y <= points[i].Y)
				{
					var (t0, y0) = points[i - 1];
					var (t1, y1) = points[i];
					return y1 > y0 ? t0 + (t1 - t0) * (y - y0) / (y1 - y0) : t1;
				}
			}

			var (ta, ya) = points[^2];
			var (tb, yb) = points[^1];
			return yb > ya ? tb + (tb - ta) / (yb - ya) * (y - yb) : tb;
		}

		/// <summary>
		/// Dims the lines outside the part being played at the time (from the last time tag passed up to
		/// the next one); null (stopped) shows every line as usual.
		/// </summary>
		private void ShowCurrentPart(double? seconds)
		{
			var part = seconds is { } now && parts.Count > 0
				? Math.Max(0, parts.FindLastIndex(p => p.Seconds <= now))
				: -1;
			if (part == currentPart)
			{
				return;
			}

			currentPart = part;
			var first = part >= 0 ? parts[part].Line : 0;
			var end = part < 0 ? lines.Count : part + 1 < parts.Count ? Math.Max(parts[part + 1].Line, first + 1) : lines.Count;
			for (var i = 0; i < lines.Count; i++)
			{
				lines[i].Opacity = lineOpacities[i] * (part < 0 || (i >= first && i < end) ? 1 : DimmedOpacity);
			}
		}

		/// <summary>
		/// The rendered lyrics' lines and where the timed parts start. A chorus's lines count one by one;
		/// a time tag inside a line starts the part at that line, and so do the headings and empty lines
		/// just above it (a part's heading belongs to it). Tags out of order are left out, as for the scrolling.
		/// </summary>
		private void FindParts(Control content)
		{
			lines = [];
			parts = [];
			currentPart = -1;
			if (content is not StackPanel root)
			{
				return;
			}

			foreach (var child in root.Children)
			{
				if (child is Border { Child: StackPanel chorus })
				{
					lines.AddRange(chorus.Children);
				}
				else
				{
					lines.Add(child);
				}
			}

			lineOpacities = lines.Select(line => line.Opacity).ToList();
			foreach (var marker in markers.OrderBy(m => m.Time))
			{
				var line = LineOf(marker.Anchor);
				while (line > 0 && (lines[line - 1].Classes.Contains(LyricsClasses.Heading) || lines[line - 1].Classes.Contains(LyricsClasses.Gap)))
				{
					line--;
				}

				if (line >= 0 && (parts.Count == 0 || (marker.Time.TotalSeconds > parts[^1].Seconds && line >= parts[^1].Line)))
				{
					parts.Add((marker.Time.TotalSeconds, line));
				}
			}

			// Before the first tag, the lyrics from the top are what's being played.
			if (parts.Count > 0 && parts[0].Line > 0)
			{
				if (parts[0].Seconds > 0)
				{
					parts.Insert(0, (0, 0));
				}
				else
				{
					parts[0] = (0, 0);
				}
			}
		}

		private int LineOf(Control anchor)
		{
			for (StyledElement? element = anchor; element != null; element = element.Parent)
			{
				if (element is Control control && lines.IndexOf(control) is var index and >= 0)
				{
					return index;
				}
			}

			return -1;
		}

		/// <summary>How many bars a nudge moves timed lyrics.</summary>
		public const int NudgeBars = 4;

		/// <summary>
		/// Moves the lyrics on (+1) or back (-1) a little, also while they scroll by themselves. Timed
		/// lyrics (with a tempo) move <see cref="NudgeBars"/> bars along the song's timeline; others one
		/// line, where a chord line with its words counts as one line and blank lines are skipped.
		/// </summary>
		public void Nudge(int direction)
		{
			if (scroller == null || IsEditing || scroller.Extent.Height <= 0 || direction == 0)
			{
				return;
			}

			direction = Math.Sign(direction);
			// Measured from where a nudge still gliding will end, so quick presses add up.
			var y = scroller.Offset.Y + NudgeRemaining(time: false);
			if (IsTimed && song.Tempo > 0 && Timeline() is { } points)
			{
				var step = direction * NudgeBars * ChordPro.BeatsPerBar(song.Beat) * 60 / song.Tempo;
				if (scrollTimer.IsEnabled)
				{
					// Playing: the song's clock moves on or back, and the lyrics with it (not before 0:00).
					var now = playbackStart + scrollClock.Elapsed.TotalSeconds + NudgeRemaining(time: true);
					StartNudge(Math.Max(step, -now), time: true);
				}
				else
				{
					var now = TimeAt(points, y + scroller.Viewport.Height / 2);
					var target = Math.Clamp(CenterAt(points, Math.Max(0, now + step)) - scroller.Viewport.Height / 2, 0, MaxOffset(scroller));
					StartNudge(target - y, time: false);
				}

				return;
			}

			// The line just below (or above) the top of the view goes to the top. A line only a few
			// pixels away (a margin) is already there.
			const double near = 6;
			var tops = LineTops();
			var lineTarget = direction > 0
				? tops.Where(top => top > y + near).Cast<double?>().FirstOrDefault() ?? MaxOffset(scroller)
				: tops.Where(top => top < y - near).Cast<double?>().LastOrDefault() ?? 0;
			StartNudge(Math.Clamp(lineTarget, 0, MaxOffset(scroller)) - y, time: false);
		}

		/// <summary>How long a nudge glides.</summary>
		public static readonly TimeSpan NudgeDuration = TimeSpan.FromMilliseconds(500);

		// A nudge glides (eased in and out) instead of jumping: it moves either the scroll offset by
		// pixels or, while timed lyrics play, the song's clock by seconds. Part of it is applied each frame.
		private readonly DispatcherTimer nudgeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
		private readonly System.Diagnostics.Stopwatch nudgeClock = new();
		private bool nudgeIsTime;
		private double nudgeTotal;
		private double nudgeApplied;

		/// <summary>What the gliding nudge (of that kind) still has to move.</summary>
		private double NudgeRemaining(bool time) => nudgeTimer.IsEnabled && nudgeIsTime == time ? nudgeTotal - nudgeApplied : 0;

		private void StartNudge(double amount, bool time)
		{
			var rest = NudgeRemaining(time);
			nudgeIsTime = time;
			nudgeTotal = rest + amount;
			nudgeApplied = 0;
			if (Math.Abs(nudgeTotal) < 0.001)
			{
				nudgeTimer.Stop();
				return;
			}

			nudgeClock.Restart();
			nudgeTimer.Start();
			NudgeStep();
		}

		private void NudgeStep()
		{
			var t = Math.Min(1, nudgeClock.Elapsed / NudgeDuration);
			// Ease in and out (cubic): starts and ends softly.
			var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
			var reached = nudgeIsTime ? nudgeTotal * eased : Math.Round(nudgeTotal * eased); // Whole pixels.
			var step = reached - nudgeApplied;
			nudgeApplied = reached;
			if (t >= 1)
			{
				nudgeTimer.Stop();
			}

			if (nudgeIsTime)
			{
				playbackStart += step;
				if (scrollTimer.IsEnabled)
				{
					ScrollStep();
				}
			}
			else if (scroller != null && step != 0)
			{
				scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(scroller.Offset.Y + step, 0, MaxOffset(scroller)));
			}
		}

		/// <summary>Drops a gliding nudge (when playing starts or stops, or the lyrics change).</summary>
		private void StopNudge() => nudgeTimer.Stop();

		/// <summary>
		/// Where each line of the lyrics starts (content y, top to bottom): a chord line with its words
		/// is one line, a wrapped line counts once per row, and blank lines and invisible places aren't lines.
		/// </summary>
		private List<double> LineTops()
		{
			var tops = new List<double>();
			if (lyricsContent == null)
			{
				return tops;
			}

			void AddRows(TextBlock block, double top)
			{
				var text = block.Text ?? "";
				var y = top;
				foreach (var row in block.TextLayout.TextLines)
				{
					var start = Math.Min(row.FirstTextSourceIndex, text.Length);
					// Plain lyrics are one text: its empty rows are blank lines.
					if (text.Length == 0 || text.Substring(start, Math.Min(row.Length, text.Length - start)).Trim().Length > 0)
					{
						tops.Add(y);
					}

					y += row.Height;
				}
			}

			if (lines.Count == 0)
			{
				if (lyricsContent is TextBlock plain)
				{
					AddRows(plain, 0);
				}

				return tops;
			}

			foreach (var line in lines)
			{
				if (line.Classes.Contains(LyricsClasses.Gap) || line.Bounds.Height <= 0
				    || line.TranslatePoint(default, lyricsContent) is not { } place)
				{
					continue;
				}

				if (line is TextBlock block && block.Inlines is not { Count: > 0 })
				{
					AddRows(block, place.Y + block.Padding.Top);
				}
				else if (line is TextBlock styled)
				{
					// Styled text (runs): its rows, without looking at the characters.
					var y = place.Y + styled.Padding.Top;
					foreach (var row in styled.TextLayout.TextLines)
					{
						tops.Add(y);
						y += row.Height;
					}
				}
				else
				{
					tops.Add(place.Y);
				}
			}

			return tops;
		}

		/// <summary>Whether the lyrics are scrolled all the way to the bottom (or fit without scrolling).</summary>
		public bool IsAtEnd => scroller is { } viewer && viewer.Offset.Y >= MaxOffset(viewer) - 1;

		private static double MaxOffset(ScrollViewer viewer) => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			scrollTimer.Stop();
			StopNudge();
		}

		private void ShowLyrics()
		{
			StopNudge();
			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);
			Control text;
			markers = [];
			timeline = null;
			if (hasLyrics && ChordPro.HasFormatting(song.Lyrics))
			{
				// Chords above the words they belong to, headings, notes, and time tags for the scrolling.
				var rendered = ChordPro.Render(song.Lyrics, fontSize: 24, lineHeight: 36, song.Tempo, song.Beat);
				text = rendered.Content;
				markers = rendered.Markers;
			}
			else
			{
				text = new TextBlock
				{
					Text = hasLyrics ? song.Lyrics : "There are no lyrics for this song yet. Add them with the pencil button.",
					FontSize = hasLyrics ? 24 : 16,
					LineHeight = hasLyrics ? 36 : double.NaN,
					Opacity = hasLyrics ? 1 : 0.7,
					TextWrapping = TextWrapping.Wrap,
				};
			}

			text.Margin = new Thickness(0, 0, 0, 48);
			lyricsContent = text;
			FindParts(text);
			var scroller = new ScrollViewer
			{
				// Transparent, so a tap anywhere in the lyrics area counts, not only on the letters.
				Background = Brushes.Transparent,
				Content = text,
			};
			// A tap (not a drag to scroll) toggles focus mode.
			scroller.Tapped += (_, e) =>
			{
				e.Handled = true;
				FocusModeToggled?.Invoke();
			};
			body.Content = scroller;
			this.scroller = scroller;
			titleActions.Content = editButton;
		}

		/// <summary>The lyrics in a text box (with the error above it, if saving fails); Save and Cancel go beside the title.</summary>
		private void ShowEditor()
		{
			SetAutoScroll(false);
			StopNudge();
			scroller = null;
			var editor = new TextBox
			{
				Text = song.Lyrics,
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MaxLength = MaxLyricsLength,
				FontSize = 18,
				VerticalContentAlignment = VerticalAlignment.Top,
				PlaceholderText = "Type or paste the lyrics",
			};
			var save = HomeView.IconButton(SaveIcon, "Save lyrics");
			// Escape cancels too (before the page's ✕).
			var cancel = EscapeKey.MarkAsClose(HomeView.IconButton(CancelIcon, "Cancel editing"), first: true);
			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));
			DockPanel.SetDock(error, Dock.Top);

			// Save and Cancel take the pencil's place at the right of the title.
			titleActions.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { save, cancel } };

			cancel.Click += (_, _) => ShowLyrics();
			save.Click += async (_, _) =>
			{
				save.IsEnabled = cancel.IsEnabled = editor.IsEnabled = false;
				error.IsVisible = false;
				try
				{
					song = await saveLyrics(editor.Text ?? string.Empty);
					ShowLyrics();
				}
				catch (Exception ex)
				{
					// The edits stay in the box, so nothing is lost; Save can be tried again.
					error.Text = $"Couldn't save. {ex.Message}";
					error.IsVisible = true;
					save.IsEnabled = cancel.IsEnabled = editor.IsEnabled = true;
				}
			};

			body.Content = new DockPanel { Margin = new Thickness(0, 0, 0, 16), Children = { error, editor } };
			editor.Focus();
		}
	}
}
