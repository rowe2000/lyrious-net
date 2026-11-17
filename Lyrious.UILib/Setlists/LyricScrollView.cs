using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's lyrics in large type, scrollable, with its key, tempo and beat. The pencil button
	/// switches to editing the song in place (name, key, tempo, beat and lyrics); Save (✓) and
	/// Cancel (✕) then take its place beside the title: Save stores the song, Cancel drops the
	/// changes. This editor is the only one for a song: Edit in the songbook and a new song open
	/// this view with it showing (<see cref="EditorOnly"/>).
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

		/// <summary>The most the server stores for a song's name and lyrics.</summary>
		private const int MaxNameLength = 100;
		private const int MaxLyricsLength = 5000;

		private static readonly string[] Keys =
		[
			"C", "C#", "Db", "D", "D#", "Eb", "E", "F", "F#", "Gb", "G", "G#", "Ab", "A", "A#", "Bb", "B",
			"Cm", "C#m", "Dm", "D#m", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "A#m", "Bbm", "Bm",
		];

		private static readonly string[] Beats = ["4/4", "3/4", "2/4", "6/8", "12/8", "5/4", "7/8"];

		private const string NoKey = "(no key)";

		/// <summary>The title of a song that has no name yet.</summary>
		private const string NewSongTitle = "New song";

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
		// While editing: the name and details as text that turns into inputs when clicked, shown in
		// the place of the page's title.
		private Control? editHeader;
		private readonly Func<SongDetails, Task<SongDetails>> saveSong;
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
		// Where the last step of timed playing put the lyrics, and on which timeline: an offset that's
		// somewhere else at the next step was scrolled by hand.
		private List<(double Seconds, double Y)>? timedPoints;
		private double timedOffset;

		// While timed lyrics play, the lines outside the current part (from the last time tag passed to
		// the next) are a little dimmer. The lyrics' lines (a chorus's lines one by one), and where each
		// part starts: (seconds, first line), in time order.
		private const double DimmedOpacity = 0.7;
		private List<Control> lines = [];
		private List<double> lineOpacities = [];
		private List<(double Seconds, int Line)> parts = [];
		private int currentPart = -1;

		// Timed lyrics with a tempo: each row's bar, in a narrow column just left of the lyrics.
		private const double BarColumnWidth = 32;
		private const double LyricLineHeight = 36;
		private Canvas? barColumn;
		private bool barsPending;
		private List<(double Top, int Bar)> shownBars = [];

		// Pinch zoom (or Ctrl/⌘ + mouse wheel): the lyrics are laid out at 1/zoom of the width and
		// drawn zoomed, so bigger text rewraps to fit. Positions found in the lyrics (time tags, line
		// tops) are unzoomed; they're multiplied by the zoom where they meet the scroll offset.
		private const double MinZoom = 0.5;
		private const double MaxZoom = 3;
		private double zoom = LyricsZoom;
		private double timelineZoom;
		private LayoutTransformControl? zoomHost;
		private bool pinching;
		private double pinchStartZoom = 1;
		private double? pendingZoom;
		private double pendingZoomAnchor;

		/// <summary>The lyrics' zoom, remembered between songs and app starts; 1 is normal size.</summary>
		public static double LyricsZoom
		{
			get => double.TryParse(App.Settings.Get(SettingKeys.LyricsZoom), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
				? Math.Clamp(value, MinZoom, MaxZoom)
				: 1;
			private set => App.Settings.Set(SettingKeys.LyricsZoom, Math.Abs(value - 1) < 0.001 ? null : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
		}

		/// <summary>
		/// Whether the song's time tags set the scrolling (instead of <see cref="ScrollSpeed"/>): each
		/// tagged place reaches the middle of the view at its time.
		/// </summary>
		public bool IsTimed => markers.Count > 0;

		/// <summary>How fast the lyrics scroll by themselves, in pixels per second (when not <see cref="IsTimed"/>).</summary>
		public double ScrollSpeed { get; set; } = 12;

		/// <summary>The song shown.</summary>
		public Guid SongId => song.Id;

		/// <summary>Whether the editor is open (so the page shouldn't be replaced under the user).</summary>
		public bool IsEditing => titleActions.Content != editButton;

		/// <summary>Whether the lyrics are scrolling by themselves.</summary>
		public bool IsAutoScrolling => scrollTimer.IsEnabled;

		/// <summary>Raised when scrolling by itself starts or stops. Reaching the end doesn't stop it.</summary>
		public event Action? AutoScrollChanged;
		private SongDetails song;

		/// <summary>Raised when the user taps the lyrics, to turn focus mode on or off.</summary>
		public event Action? FocusModeToggled;

		/// <summary>
		/// Whether the view is there only for its editor (Edit in the songbook, a new song): whoever
		/// opened it shows another page when the editing is saved or cancelled.
		/// </summary>
		public bool EditorOnly { get; init; }

		/// <summary>Raised with the stored song after the editor saved it (the lyrics are showing again).</summary>
		public event Action<SongDetails>? SongSaved;

		/// <summary>Raised when the user cancels the editor (the lyrics are showing again).</summary>
		public event Action? EditCancelled;

		/// <param name="saveSong">Stores the edited song and returns it as stored; throws if that fails.</param>
		/// <param name="startEditing">Opens with the editor showing, e.g. for a new song or Edit in the songbook.</param>
		public LyricScrollView(SongDetails song, Func<SongDetails, Task<SongDetails>> saveSong, bool startEditing = false)
		{
			this.song = song;
			this.saveSong = saveSong;

			editButton = HomeView.IconButton(EditIcon, "Edit song");
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

			Content = PageLayout.SongPage(TitleOf(song), body, PageLayout.SongInfo(song), titleActions);
			ShowTitle();
		}

		private static string TitleOf(SongDetails song) => string.IsNullOrWhiteSpace(song.Name) ? NewSongTitle : song.Name;

		/// <summary>The song's name and details in the page's header, after they were edited.</summary>
		private void ShowSongHeader()
		{
			if (Content is Control page)
			{
				PageLayout.SetSongTitle(page, TitleOf(song), PageLayout.SongInfo(song));
			}
		}

		/// <summary>
		/// Shows the song's name and details if the setting says so; always while editing (Save and
		/// Cancel are there) and for a song without lyrics (nothing else would say which song it is).
		/// </summary>
		private void ShowTitle()
		{
			if (Content is Control page)
			{
				PageLayout.SetSongTitleContent(page, editHeader);
				PageLayout.SetSongTitleShown(page, DisplaySettings.ShowSongHeader || IsEditing || string.IsNullOrWhiteSpace(song.Lyrics));
			}
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			DisplaySettings.ShowSongHeaderChanged += ShowTitle;
			ShowTitle();
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
				timedPoints = null;
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
				// Scrolled by hand since the last step (wheel, drag, scroll bar): the song's clock goes
				// along, so playing carries on from there instead of pulling the lyrics back. Not when the
				// layout changed (zoom, resize): then the same time is simply somewhere else.
				if (ReferenceEquals(points, timedPoints) && Math.Abs(scroller.Offset.Y - timedOffset) >= 1)
				{
					now = TimeAt(points, scroller.Offset.Y + scroller.Viewport.Height / 2);
					playbackStart = now - scrollClock.Elapsed.TotalSeconds;
					StopNudge(); // A gliding nudge was measured from where the lyrics were.
				}

				ShowCurrentPart(now);
				var target = Math.Round(Math.Clamp(CenterAt(points, now) - scroller.Viewport.Height / 2, 0, MaxOffset(scroller)));
				scroller.Offset = new Vector(scroller.Offset.X, target);
				timedPoints = points;
				timedOffset = target;
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

			if (timeline != null && timelineFor == scroller.Viewport && timelineZoom == zoom)
			{
				return timeline;
			}

			var points = new List<(double Seconds, double Y)>();
			foreach (var marker in markers.OrderBy(m => m.Time))
			{
				if (marker.Anchor.TranslatePoint(new Point(0, marker.Anchor.Bounds.Height / 2), lyricsContent) is { } place
				    && (points.Count == 0 || (marker.Time.TotalSeconds > points[^1].Seconds && place.Y >= points[^1].Y)))
				{
					points.Add((marker.Time.TotalSeconds, place.Y * zoom));
				}
			}

			if (points.Count == 0)
			{
				return null;
			}

			// At 0:00 the lyrics start at the top: the middle of the view shows the content's y = half its height.
			var half = scroller.Viewport.Height / 2;
			if (points[0].Seconds > 0)
			{
				points.Insert(0, (0, Math.Min(half, points[0].Y)));
			}

			// Tags in the top half of the lyrics can't reach the middle of the view (the lyrics can't
			// scroll above their top), so they'd hold the lyrics still. Instead the lyrics move on from
			// the start, to reach the first tag that can be in the middle at its time.
			if (points[0].Y < half)
			{
				points[0] = (points[0].Seconds, half);
				while (points.Count > 1 && points[1].Y <= half)
				{
					points.RemoveAt(1);
				}
			}

			timeline = points;
			timelineFor = scroller.Viewport;
			timelineZoom = zoom;
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
			var tops = LineTops().Select(top => top * zoom).ToList();
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

		/// <summary>Shows the rows' bars once layout has settled (rows wrap with the width).</summary>
		private void ScheduleBars()
		{
			if (barColumn == null || barsPending)
			{
				return;
			}

			barsPending = true;
			Dispatcher.UIThread.Post(() =>
			{
				barsPending = false;
				ShowBars();
			}, DispatcherPriority.Background);
		}

		/// <summary>
		/// The bar of each row with a time tag (bar or time stamp), counted from 1: the tag's time in
		/// whole bars, plus one, so a row tagged [5] shows 5. A row with several tags shows the first;
		/// a tag on a line of its own (no words) goes with the row below it.
		/// </summary>
		private void ShowBars()
		{
			if (barColumn == null || lyricsContent == null || scroller == null || scroller.Extent.Height <= 0)
			{
				return;
			}

			var barSeconds = 60.0 / song.Tempo * ChordPro.BeatsPerBar(song.Beat);
			var rows = BarRows();
			var bars = new List<(double Top, int Bar)>();
			var taken = new HashSet<int>();
			foreach (var marker in markers.OrderBy(m => m.Time))
			{
				if (marker.Anchor.TranslatePoint(new Point(0, marker.Anchor.Bounds.Height / 2), lyricsContent) is not { } place)
				{
					continue;
				}

				// A tag with words: its row. One on a line of its own (an empty mark where the rows
				// meet): the row below.
				var row = marker.Anchor.Bounds.Height > 0
					? rows.FindIndex(r => place.Y >= r.Top - 0.5 && place.Y <= r.Bottom + 0.5)
					: -1;
				if (row < 0)
				{
					row = rows.FindIndex(r => r.Top >= place.Y - 0.5 && r.Bottom > place.Y + 0.5);
				}

				if (row >= 0 && taken.Add(row))
				{
					bars.Add((Math.Round(rows[row].TextY - LyricLineHeight / 2), (int)Math.Round(marker.Time.TotalSeconds / barSeconds) + 1));
				}
			}

			bars.Sort((a, b) => a.Top.CompareTo(b.Top));
			if (bars.SequenceEqual(shownBars))
			{
				return;
			}

			shownBars = bars;
			barColumn.Children.Clear();
			foreach (var (top, bar) in bars)
			{
				var label = new TextBlock
				{
					Text = bar.ToString(System.Globalization.CultureInfo.InvariantCulture),
					FontSize = 14,
					Width = BarColumnWidth,
					Height = LyricLineHeight,
					LineHeight = LyricLineHeight,
					TextAlignment = TextAlignment.Right,
					Opacity = 0.5,
					IsHitTestVisible = false,
				};
				Canvas.SetTop(label, top);
				barColumn.Children.Add(label);
			}
		}

		/// <summary>
		/// The rows of the lyrics, headings included (a wrapped line has several), top to bottom: where
		/// each starts and ends, and the middle of its words (below the chords on a chord line).
		/// </summary>
		private List<(double Top, double Bottom, double TextY)> BarRows()
		{
			var rows = new List<(double Top, double Bottom, double TextY)>();
			if (lyricsContent == null)
			{
				return rows;
			}

			foreach (var line in lines)
			{
				if (line.Classes.Contains(LyricsClasses.Gap) || line.Bounds.Height <= 0
				    || line.TranslatePoint(default, lyricsContent) is not { } place)
				{
					continue;
				}

				if (line is TextBlock block)
				{
					var y = place.Y + block.Padding.Top;
					foreach (var row in block.TextLayout.TextLines)
					{
						rows.Add((y, y + row.Height, y + row.Height / 2));
						y += row.Height;
					}
				}
				else if (line is Panel panel)
				{
					// A chord line: its pieces in rows (it wraps between words). A row's pieces end at
					// its bottom; a word without a chord starts lower (its margin is the chord's room).
					foreach (var row in panel.Children.Where(child => child.IsVisible)
						         .GroupBy(child => Math.Round(child.Bounds.Bottom))
						         .OrderBy(group => group.Key))
					{
						var top = place.Y + row.Min(child => child.Bounds.Top - child.Margin.Top);
						var bottom = place.Y + row.Max(child => child.Bounds.Bottom);
						rows.Add((top, bottom, bottom - LyricLineHeight / 2));
					}
				}
			}

			return rows;
		}

		/// <summary>Whether the lyrics are scrolled all the way to the bottom (or fit without scrolling).</summary>
		public bool IsAtEnd => scroller is { } viewer && viewer.Offset.Y >= MaxOffset(viewer) - 1;

		private static double MaxOffset(ScrollViewer viewer) => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			DisplaySettings.ShowSongHeaderChanged -= ShowTitle;
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

			// Timed lyrics with a tempo: each row's bar, just left of it.
			Control content = text;
			barColumn = null;
			shownBars = [];
			if (markers.Count > 0 && song.Tempo > 0)
			{
				barColumn = new Canvas { Width = BarColumnWidth, Margin = new Thickness(0, 0, 12, 0), ClipToBounds = false };
				Grid.SetColumn(text, 1);
				content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { barColumn, text } };
				text.SizeChanged += (_, _) => ScheduleBars();
			}

			zoomHost = new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = content };
			var scroller = new ScrollViewer
			{
				// Transparent, so a tap anywhere in the lyrics area counts, not only on the letters.
				Background = Brushes.Transparent,
				Content = zoomHost,
			};
			AddZoomGestures(scroller);
			// The timeline depends on the view's height too.
			scroller.SizeChanged += (_, _) => ScheduleBars();
			// A tap (not a drag to scroll) toggles focus mode.
			scroller.Tapped += (_, e) =>
			{
				e.Handled = true;
				FocusModeToggled?.Invoke();
			};
			body.Content = scroller;
			this.scroller = scroller;
			titleActions.Content = editButton;
			editHeader = null;
			ShowTitle();
		}

		/// <summary>Pinch (touch screen or touchpad) and Ctrl/⌘ + mouse wheel zoom the lyrics.</summary>
		private void AddZoomGestures(ScrollViewer viewer)
		{
			viewer.GestureRecognizers.Add(new PinchGestureRecognizer());
			viewer.AddHandler(InputElement.PinchEvent, (_, e) =>
			{
				if (!pinching)
				{
					pinching = true;
					pinchStartZoom = pendingZoom ?? zoom; // The pinch's scale is from where it started.
				}

				e.Handled = true;
				RequestZoom(pinchStartZoom * e.Scale, e.ScaleOrigin.Y);
			});
			viewer.AddHandler(InputElement.PinchEndedEvent, (_, _) => pinching = false);
			// macOS touchpads: a magnify gesture, its delta the change (0.1 = 10 % bigger).
			viewer.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, (_, e) =>
			{
				e.Handled = true;
				RequestZoom((pendingZoom ?? zoom) * (1 + e.Delta.X), e.GetPosition(viewer).Y);
			});
			// Before the scroll viewer scrolls with it.
			viewer.AddHandler(PointerWheelChangedEvent, (_, e) =>
			{
				if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Delta.Y == 0)
				{
					return;
				}

				e.Handled = true;
				RequestZoom((pendingZoom ?? zoom) * Math.Pow(1.1, Math.Sign(e.Delta.Y)), e.GetPosition(viewer).Y);
			}, RoutingStrategies.Tunnel);
		}

		/// <summary>
		/// Zooms the lyrics, keeping what's at <paramref name="anchorY"/> (in the view) in place. Quick
		/// changes (a pinch sends many) are applied together, once per layout.
		/// </summary>
		private void RequestZoom(double value, double anchorY)
		{
			value = Math.Clamp(value, MinZoom, MaxZoom);
			var first = pendingZoom == null;
			pendingZoom = value;
			pendingZoomAnchor = anchorY;
			if (first)
			{
				Dispatcher.UIThread.Post(ApplyZoom, DispatcherPriority.Input);
			}
		}

		private void ApplyZoom()
		{
			if (pendingZoom is not { } value || scroller == null || zoomHost == null)
			{
				pendingZoom = null;
				return;
			}

			pendingZoom = null;
			if (Math.Abs(value - zoom) < 0.001)
			{
				return;
			}

			// Where the anchor is, as a share of the lyrics' height: the lines rewrap, so this keeps the
			// same words there better than scaling the offset.
			var anchor = Math.Clamp(pendingZoomAnchor, 0, scroller.Viewport.Height);
			var share = scroller.Extent.Height > 0 ? (scroller.Offset.Y + anchor) / scroller.Extent.Height : 0;
			zoom = value;
			LyricsZoom = value;
			zoomHost.LayoutTransform = new ScaleTransform(zoom, zoom);
			scroller.UpdateLayout();
			if (!scrollTimer.IsEnabled || !IsTimed)
			{
				scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(Math.Round(share * scroller.Extent.Height - anchor), 0, MaxOffset(scroller)));
			}

			ScheduleBars();
		}

		/// <summary>
		/// The song's name, key, tempo and beat, and its lyrics in a text box (with the error above, if
		/// saving fails); Save and Cancel go beside the title. This is the one place a song is edited.
		/// </summary>
		private void ShowEditor()
		{
			SetAutoScroll(false);
			StopNudge();
			scroller = null;
			var name = new TextBox { Text = song.Name, MaxLength = MaxNameLength, PlaceholderText = "Song name" };
			var key = Choice(Keys, song.Key, NoKey);
			var beat = Choice(Beats, song.Beat, null);
			var tempo = new NumericUpDown { Value = (decimal)Math.Clamp(song.Tempo, 20, 300), Minimum = 20, Maximum = 300, Increment = 1, FormatString = "0", MinWidth = 140 };
			var editor = new TextBox
			{
				Text = song.Lyrics,
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MaxLength = MaxLyricsLength,
				FontSize = 18,
				MinHeight = 120,
				VerticalContentAlignment = VerticalAlignment.Top,
				PlaceholderText = "Type or paste the lyrics",
			};
			var save = HomeView.IconButton(SaveIcon, "Save song");
			// Escape cancels too (before the page's ✕).
			var cancel = EscapeKey.MarkAsClose(HomeView.IconButton(CancelIcon, "Cancel editing"), first: true);
			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));

			// The name is plain text, as in the title; clicking it puts the text box in its place, and
			// leaving the box (or Enter) brings the text back.
			var nameText = new TextBlock
			{
				FontSize = 28,
				FontWeight = FontWeight.Bold,
				TextWrapping = TextWrapping.Wrap,
				Background = Brushes.Transparent,
				Cursor = new Cursor(StandardCursorType.Hand),
			};
			ToolTip.SetTip(nameText, "Click to rename");
			var nameSlot = new ContentControl { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 0) };
			name.FontSize = 22;
			name.FontWeight = FontWeight.Bold;
			name.MinWidth = 240;

			void ShowNameText()
			{
				nameText.Text = string.IsNullOrWhiteSpace(name.Text) ? NewSongTitle : name.Text.Trim();
				nameSlot.Content = nameText;
			}

			void ShowNameBox()
			{
				nameSlot.Content = name;
				Dispatcher.UIThread.Post(() =>
				{
					name.Focus();
					name.SelectAll();
				});
			}

			nameText.Tapped += (_, e) =>
			{
				e.Handled = true;
				ShowNameBox();
			};
			name.LostFocus += (_, _) => ShowNameText();
			name.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					editor.Focus();
				}
			};

			// Key, tempo and beat likewise: text until clicked, then their inputs, side by side on one
			// line (so without labels and spin buttons), until none of them has the focus any more.
			key.MinWidth = 0;
			beat.MinWidth = 0;
			tempo.MinWidth = 0;
			tempo.Width = 72;
			tempo.ShowButtonSpinner = false;
			ToolTip.SetTip(key, "Key");
			ToolTip.SetTip(tempo, "Tempo (bpm)");
			ToolTip.SetTip(beat, "Beat");
			var detailsInputs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { key, tempo, beat } };

			// The song with the key, tempo and beat as they are in the inputs.
			SongDetails WithDetails() => song with
			{
				Key = key.SelectedItem as string is { } k && k != NoKey ? k : "",
				Beat = beat.SelectedItem as string ?? song.Beat,
				Tempo = (float)(tempo.Value ?? (decimal)song.Tempo),
			};

			var info = PageLayout.SongInfo(song);
			var detailsText = new TextBlock
			{
				Text = string.IsNullOrEmpty(info) ? "Set key, tempo and beat" : info,
				Opacity = 0.7,
				Background = Brushes.Transparent,
				Cursor = new Cursor(StandardCursorType.Hand),
				Margin = new Thickness(0, 0, 0, 5),
			};
			ToolTip.SetTip(detailsText, "Click to change key, tempo and beat");
			var detailsSlot = new ContentControl { VerticalAlignment = VerticalAlignment.Bottom, Content = detailsText };
			detailsText.Tapped += (_, e) =>
			{
				e.Handled = true;
				detailsSlot.Content = detailsInputs;
				Dispatcher.UIThread.Post(() => key.Focus());
			};

			// Whether the focus is in one of the three inputs: in the input itself, or in the open
			// list of a drop-down (which is in a window of its own, but logically inside the drop-down).
			bool FocusInDetails()
			{
				if (key.IsDropDownOpen || beat.IsDropDownOpen)
				{
					return true;
				}

				var focused = TopLevel.GetTopLevel(detailsInputs)?.FocusManager?.GetFocusedElement();
				if (focused is Visual visual && (visual == detailsInputs || detailsInputs.IsVisualAncestorOf(visual)))
				{
					return true;
				}

				for (var logical = focused as ILogical; logical != null; logical = logical.LogicalParent)
				{
					if (logical == detailsInputs)
					{
						return true;
					}
				}

				return false;
			}

			// Checked a moment after the focus left one of them: by then it is where it went.
			void CheckDetailsFocus() => Dispatcher.UIThread.Post(() =>
			{
				if (detailsSlot.Content == detailsInputs && !FocusInDetails())
				{
					var edited = PageLayout.SongInfo(WithDetails());
					detailsText.Text = string.IsNullOrEmpty(edited) ? "Set key, tempo and beat" : edited;
					detailsSlot.Content = detailsText;
				}
			});
			detailsInputs.LostFocus += (_, _) => CheckDetailsFocus();
			key.DropDownClosed += (_, _) => CheckDetailsFocus();
			beat.DropDownClosed += (_, _) => CheckDetailsFocus();

			editHeader = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { nameSlot, detailsSlot } };

			// Save and Cancel take the pencil's place at the right of the title.
			titleActions.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { save, cancel } };
			ShowTitle();

			// A song needs a name.
			save.IsEnabled = !string.IsNullOrWhiteSpace(song.Name);
			name.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);

			cancel.Click += (_, _) =>
			{
				ShowLyrics();
				EditCancelled?.Invoke();
			};
			save.Click += async (_, _) =>
			{
				var edited = WithDetails() with
				{
					Name = name.Text!.Trim(),
					Lyrics = editor.Text ?? string.Empty,
				};

				var inputs = new Control[] { save, cancel, name, key, tempo, beat, editor };
				foreach (var input in inputs)
				{
					input.IsEnabled = false;
				}

				error.IsVisible = false;
				try
				{
					song = await saveSong(edited);
					ShowSongHeader();
					ShowLyrics();
					SongSaved?.Invoke(song);
				}
				catch (Exception ex)
				{
					// The edits stay in the boxes, so nothing is lost; Save can be tried again.
					error.Text = $"Couldn't save. {ex.Message}";
					error.IsVisible = true;
					foreach (var input in inputs)
					{
						input.IsEnabled = true;
					}
				}
			};

			// The name and details are in the title; the lyrics take the page.
			Grid.SetRow(editor, 1);
			body.Content = new Grid
			{
				Margin = new Thickness(0, 0, 0, 16),
				RowDefinitions = new RowDefinitions("Auto,*"),
				Children = { error, editor },
			};

			// A song without a name yet (a new one) starts in the name's box, others in the lyrics.
			if (string.IsNullOrWhiteSpace(song.Name))
			{
				nameSlot.Content = name;
				name.AttachedToVisualTree += (_, _) => name.Focus();
			}
			else
			{
				ShowNameText();
				editor.Focus();
			}
		}

		/// <summary>A drop-down of <paramref name="options"/>, with the current value added if it isn't one of them.</summary>
		private static ComboBox Choice(IEnumerable<string> options, string current, string? none)
		{
			var items = new List<string>();
			if (none != null)
			{
				items.Add(none);
			}

			items.AddRange(options);
			if (!string.IsNullOrWhiteSpace(current) && !items.Contains(current))
			{
				items.Add(current);
			}

			return new ComboBox
			{
				ItemsSource = items,
				SelectedItem = string.IsNullOrWhiteSpace(current) ? none ?? items.FirstOrDefault() : current,
				MinWidth = 120,
			};
		}

		private static Control Field(string label, Control input) => new StackPanel
		{
			Spacing = 4,
			Margin = new Thickness(0, 0, 16, 0),
			Children = { new TextBlock { Text = label, FontSize = 13, Opacity = 0.75 }, input },
		};
	}
}
