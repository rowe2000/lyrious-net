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
using Avalonia.Platform.Storage;
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
	/// Tapping the lyrics once asks for focus mode on or off (<see cref="FocusModeToggled"/>: off when
	/// it's on, on only while the lyrics scroll by themselves); in
	/// focus mode only the song's name, key, tempo, beat and lyrics are shown. The lyrics can also
	/// scroll by themselves (<see cref="SetAutoScroll"/>), at <see cref="ScrollSpeed"/>.
	/// </summary>
	public sealed class LyricScrollView : UserControl
	{
		// A pencil: the body, its tip, and the line where the tip meets the body.
		private const string EditIcon = "M4,20 L8,19 L19,8 L16,5 L5,16 Z M14,7 L17,10";
		private const string CancelIcon = "M6,6 L18,18 M18,6 L6,18";
		// A grid of three columns: the chord grid.
		private const string ChordGridIcon = "M4,5 H20 V19 H4 Z M9.3,5 V19 M14.7,5 V19";

		/// <summary>The most the server stores for a song's name and lyrics.</summary>
		private const int MaxNameLength = 100;
		private const int MaxLyricsLength = 5000;
		// As the API keeps it.
		private const int MaxMediaLinkLength = 500;

		private static readonly string[] Keys =
		[
			"C", "C#", "Db", "D", "D#", "Eb", "E", "F", "F#", "Gb", "G", "G#", "Ab", "A", "A#", "Bb", "B",
			"Cm", "C#m", "Dm", "D#m", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "A#m", "Bbm", "Bm",
		];

		private static readonly string[] Beats = ["4/4", "3/4", "2/4", "6/8", "12/8", "5/4", "7/8"];

		private const string NoKey = "(no key)";
		private const string NoOriginalKey = "(not set)";

		/// <summary>The title of a song that has no name yet.</summary>
		private const string NewSongTitle = "New song";

		private readonly ContentControl body = new();
		private readonly Button editButton;
		private readonly Button chordGridButton;
		// Beside the pencil: the song's media links (see MediaButtons); a change to them is saved at once.
		private readonly StackPanel mediaButtons;
		private readonly Action showMediaLink;
		private readonly Func<IStorageFile, Task> addSoundBesidePencil;
		private readonly Func<string, Task> addLinkBesidePencil;
		// The media button and the pencil, shown at the right of the title while not editing.
		private readonly StackPanel lyricsActions;
		// At the right of the title: the media buttons, the chord grid and the pencil, or while editing
		// the media buttons, Save and Cancel. All are icon buttons of the same size, as many either way,
		// so the title doesn't move or rewrap when editing starts or stops. At least room for three
		// (40 = a 24-pixel icon plus 8 padding on each side, 4 apart); more media links take more.
		// The time now (hours and minutes, as the device writes them), at the right of the toolbar.
		private readonly TextBlock clock = new()
		{
			FontSize = 20,
			FontWeight = FontWeight.SemiBold,
			Opacity = 0.85,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 4, 0),
		};
		private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(5) };

		private void ShowTime() => clock.Text = DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);

		private readonly ContentControl titleActions = new()
		{
			MinWidth = 128,
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

		/// <summary>
		/// Whether lyrics with chords show as a chord grid (chords only, in equal columns by bar) instead
		/// of chords over the words; remembered on this device for every song.
		/// </summary>
		public static bool ChordGrid
		{
			get => App.Settings.Get(SettingKeys.ChordGrid) == "true";
			private set => App.Settings.Set(SettingKeys.ChordGrid, value ? "true" : null);
		}

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
		public bool IsEditing => titleActions.Content != lyricsActions;

		/// <summary>Whether the lyrics are scrolling by themselves.</summary>
		public bool IsAutoScrolling => scrollTimer.IsEnabled;

		/// <summary>Raised when scrolling by itself starts or stops. Reaching the end doesn't stop it.</summary>
		public event Action? AutoScrollChanged;
		private SongDetails song;

		/// <summary>
		/// The band's MIDI expressions (see <see cref="Midi"/>), for the editor to point out what in the
		/// lyrics' MIDI tags can't be sent; set once they're read.
		/// </summary>
		public IReadOnlyList<MidiExpression>? MidiExpressions
		{
			get => midiExpressions;
			set
			{
				midiExpressions = value;
				MidiExpressionsChanged?.Invoke();
			}
		}

		private IReadOnlyList<MidiExpression>? midiExpressions;
		private event Action? MidiExpressionsChanged;

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

		/// <summary>Gets the tags used in the songbook, offered in a popup while the song's tags are typed; null offers none.</summary>
		public Func<Task<IReadOnlyList<string>>>? LoadUsedTags { get; set; }

		/// <param name="saveSong">Stores the edited song and returns it as stored; throws if that fails.</param>
		/// <param name="startEditing">Opens with the editor showing, e.g. for a new song or Edit in the songbook.</param>
		public LyricScrollView(SongDetails song, Func<SongDetails, Task<SongDetails>> saveSong, bool startEditing = false)
		{
			this.song = song;
			clockTimer.Tick += (_, _) => ShowTime();
			this.saveSong = saveSong;
			// Where a song's sound file was is remembered until another song is shown.
			if (soundResume?.SongId != song.Id)
			{
				soundResume = null;
			}


			editButton = HomeView.IconButton(EditIcon, "Edit song");
			editButton.Click += (_, _) => ShowEditor();
			// Chords only, in bar columns, or chords over the words (remembered for every song).
			chordGridButton = HomeView.IconButton(ChordGridIcon, "Chord grid");
			chordGridButton.Click += (_, _) =>
			{
				ChordGrid = !ChordGrid;
				ShowLyrics();
			};
			(mediaButtons, showMediaLink, addSoundBesidePencil, addLinkBesidePencil) = MediaButtons(
				() => this.song.MediaLink,
				async link =>
				{
					var before = this.song.MediaLink;
					this.song = await this.saveSong(this.song with { MediaLink = link });
					UploadNewSound(before, this.song);
					SongSaved?.Invoke(this.song);
				},
				() => this.song.Name,
				() => this.song.Lyrics,
				() => this.song.Artist,
				ShowProblemAt);
			lyricsActions = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				HorizontalAlignment = HorizontalAlignment.Right,
				Children = { mediaButtons, chordGridButton, editButton },
			};
			// Sound files and links dropped anywhere on the view are added to the song's media links.
			// Tunnelling: seen before the editor's text box, which would type a dropped link into the lyrics.
			DragDrop.SetAllowDrop(this, true);
			AddHandler(DragDrop.DragOverEvent, OnMediaDraggedOver, RoutingStrategies.Tunnel, handledEventsToo: true);
			AddHandler(DragDrop.DropEvent, OnMediaDropped, RoutingStrategies.Tunnel, handledEventsToo: true);
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

			// (The media player is the app's, over every page: see MediaPlayerLayer.)
			// The time at the right of the toolbar (also in focus mode, so it shows while playing).
			var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { titleActions, clock } };
			Content = PageLayout.SongPage(TitleOf(song), body, PageLayout.SongInfo(song), toolbar);
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
			ShowTime();
			clockTimer.Start();
			ShowTitle();
			if (App.SoundStore is { } sounds)
			{
				sounds.PlayingChanged += OnSoundPlayingChanged;
				songSoundPlayed = SongSoundPlays;
			}
		}

		/// <summary>
		/// Shows or hides everything but the song's name, details and lyrics (that is, the media
		/// button and the pencil). They're made invisible rather than removed, so the title and lyrics
		/// don't move.
		/// </summary>
		public void SetFocusMode(bool focused)
		{
			lyricsActions.Opacity = focused ? 0 : 1;
			lyricsActions.IsHitTestVisible = !focused;
		}

		/// <summary>Scrolls the lyrics back to the start; if they're scrolling by themselves, they go on from there.</summary>
		public void ScrollToStart()
		{
			// The song's sound file goes back to its start too (playing: the lyrics follow it from there).
			if (!RestartMedia())
			{
				ScrolledByHand();
			}

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

			// The song's sound file plays: the lyrics go where it is.
			if (FollowMedia(scroller))
			{
				return;
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

			ScrolledByHand();

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

		/// <summary>To the start of the lyrics (the Home key), also while they scroll by themselves.</summary>
		public void ScrollHome()
		{
			if (IsTimed && scrollTimer.IsEnabled)
			{
				ScrollToStart(); // The song's clock starts again too.
			}
			else
			{
				GlideTo(0);
			}
		}

		/// <summary>To the end of the lyrics (the End key), also while they scroll by themselves.</summary>
		public void ScrollEnd()
		{
			GlideTo(double.MaxValue);
			ScrolledByHand();
		}

		/// <summary>
		/// A page on (+1) or back (-1): nearly the height of the view, so the last lines stay in sight
		/// (the Page Down and Page Up keys). Also while the lyrics scroll by themselves.
		/// </summary>
		public void ScrollPage(int direction)
		{
			if (scroller != null)
			{
				ScrolledByHand();
				GlideTo(scroller.Offset.Y + NudgeRemaining(time: false) + Math.Sign(direction) * scroller.Viewport.Height * 0.9);
			}
		}

		/// <summary>
		/// Glides the lyrics to the offset (kept within the lyrics). While timed lyrics play they jump
		/// there instead, and the song's clock goes along as for scrolling by hand (see ScrollStep): a
		/// glide would be taken for that at its first step, and dropped.
		/// </summary>
		private void GlideTo(double target)
		{
			if (scroller == null || IsEditing || scroller.Extent.Height <= 0)
			{
				return;
			}

			target = Math.Round(Math.Clamp(target, 0, MaxOffset(scroller)));
			if (IsTimed && scrollTimer.IsEnabled)
			{
				StopNudge();
				scroller.Offset = new Vector(scroller.Offset.X, target);
				return;
			}

			// From where a glide still going will end, so quick presses add up.
			StartNudge(target - (scroller.Offset.Y + NudgeRemaining(time: false)), time: false);
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
			clockTimer.Stop();
			if (App.SoundStore is { } sounds)
			{
				sounds.PlayingChanged -= OnSoundPlayingChanged;
			}
			scrollTimer.Stop();
			StopNudge();
			// A sound started here stops with the song (going to the next one, say); where it was is
			// remembered, for when this song is shown again before another one.
			if (startedSound)
			{
				startedSound = false;
				RememberSoundPosition();
				App.SoundStore?.Stop();
			}
		}

		private void ShowLyrics()
		{
			StopNudge();
			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);
			Control text;
			markers = [];
			timeline = null;
			// The chord grid button only for lyrics with chords; highlighted while the grid is on.
			// Lyrics with chords, or bar lines ("|"), can be shown as a chord grid.
			var hasChords = hasLyrics && (ChordPro.IsChordPro(song.Lyrics) || ChordPro.HasBarLines(song.Lyrics));
			var grid = hasChords && ChordGrid;
			chordGridButton.IsVisible = hasChords;
			if (grid)
			{
				chordGridButton.Classes.Add("accent");
				chordGridButton.ClearValue(BackgroundProperty);
			}
			else
			{
				chordGridButton.Classes.Remove("accent");
				chordGridButton.Background = Brushes.Transparent;
			}

			ToolTip.SetTip(chordGridButton, grid ? "Show chords over the words" : "Show a chord grid (chords only, by bar)");

			if (hasLyrics && ChordPro.HasFormatting(song.Lyrics))
			{
				// Chords above the words they belong to (or the chord grid), headings, notes, and time tags for the scrolling.
				// The lyrics are written in the original key; the chords are shown in the playing key.
				var transposed = Transpose.Chords(song.Lyrics, Transpose.Semitones(song.OriginalKey, song.PlayingKey), song.PlayingKey);
				var rendered = ChordPro.Render(transposed, fontSize: 24, lineHeight: 36, song.Tempo, song.Beat, chordGrid: grid);
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
			// The column's room is kept without bars too, so all songs' lyrics start at the same place.
			var column = new Canvas { Width = BarColumnWidth, Margin = new Thickness(0, 0, 12, 0), ClipToBounds = false };
			Grid.SetColumn(text, 1);
			content = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,*"),
				Children = { column, text },
				// As wide as the song's widest line (still wrapping when that's wider than the view), in
				// the middle; the chord grid takes the whole width (its columns share it).
				HorizontalAlignment = grid ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
			};
			if (markers.Count > 0 && song.Tempo > 0)
			{
				barColumn = column;
				text.SizeChanged += (_, _) => ScheduleBars();
			}

			// Centred too (outside the zoom): the zoom lays its content out from the left, at the content's own size.
			zoomHost = new LayoutTransformControl
			{
				LayoutTransform = new ScaleTransform(zoom, zoom),
				Child = content,
				HorizontalAlignment = grid ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
			};
			var scroller = new ScrollViewer
			{
				// Transparent, so a tap anywhere in the lyrics area counts, not only on the letters.
				Background = Brushes.Transparent,
				Content = zoomHost,
			};
			AddZoomGestures(scroller);
			// Scrolled by hand (wheel or touchpad, a drag, the scroll bar): the song's sound file goes along.
			scroller.AddHandler(PointerWheelChangedEvent, (_, _) =>
			{
				if (!wheelZooms)
				{
					ScrolledByHand();
				}
			}, RoutingStrategies.Bubble, handledEventsToo: true);
			scroller.AddHandler(InputElement.ScrollGestureEvent, (_, _) => ScrolledByHand(), RoutingStrategies.Bubble, handledEventsToo: true);
			scroller.AddHandler(Avalonia.Controls.Primitives.Thumb.DragDeltaEvent, (_, _) => ScrolledByHand(), RoutingStrategies.Bubble, handledEventsToo: true);
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
			titleActions.Content = lyricsActions;
			// The link may have been changed in the editor.
			showMediaLink();
			addDroppedSound = addSoundBesidePencil;
			addDroppedLink = addLinkBesidePencil;
			editHeader = null;
			ShowTitle();
		}

		/// <summary>
		/// How much of a pinch's change in size goes to the zoom: 1 follows the fingers exactly, which
		/// is too touchy for lyrics; 0.6 needs a wider pinch for the same change.
		/// </summary>
		private const double PinchDamping = 0.6;

		/// <summary>The longest pause (ms) between wheel steps of one gesture (a scroll with its inertia, or a zoom).</summary>
		private const ulong WheelGestureGap = 200;

		// The wheel gesture going on: when its last step came, and whether it zooms (else it scrolls).
		private ulong lastWheelTimestamp;
		private bool wheelZooms;

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
				RequestZoom(pinchStartZoom * Math.Pow(e.Scale, PinchDamping), e.ScaleOrigin.Y);
			});
			viewer.AddHandler(InputElement.PinchEndedEvent, (_, _) => pinching = false);
			// macOS touchpads: a magnify gesture, its delta the change (0.1 = 10 % bigger).
			viewer.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, (_, e) =>
			{
				e.Handled = true;
				RequestZoom((pendingZoom ?? zoom) * Math.Pow(1 + e.Delta.X, PinchDamping), e.GetPosition(viewer).Y);
			});
			// Before the scroll viewer scrolls with it. Zoom or scroll is decided once per wheel gesture
			// (wheel steps close together): by Ctrl/⌘ at its first step. So a quick scroll's inertia that
			// goes on after Ctrl/⌘ is pressed keeps scrolling, and a zoom keeps zooming to its end.
			viewer.AddHandler(PointerWheelChangedEvent, (_, e) =>
			{
				var newGesture = e.Timestamp - lastWheelTimestamp > WheelGestureGap;
				lastWheelTimestamp = e.Timestamp;
				if (newGesture)
				{
					wheelZooms = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
				}

				if (!wheelZooms || e.Delta.Y == 0)
				{
					return;
				}

				e.Handled = true;
				// A mouse wheel's notch (1) is 10 %. A touchpad's pinch on Windows comes as many small
				// wheel steps with Ctrl: each counts for its size (damped like a pinch), not a whole notch.
				var notches = Math.Clamp(e.Delta.Y, -1, 1);
				if (Math.Abs(notches) < 1)
				{
					notches *= PinchDamping;
				}

				RequestZoom((pendingZoom ?? zoom) * Math.Pow(1.1, notches), e.GetPosition(viewer).Y);
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
		/// Once the song is saved with a sound file kept here as its new media link, the file goes to
		/// the server too, for the band's other devices (see <see cref="SoundSync"/>). Done after the
		/// save, as a new song only gets its id then.
		/// </summary>
		private static void UploadNewSound(string previousLink, SongDetails saved)
		{
			var before = MediaLinks.SoundsOf(previousLink).Select(sound => sound.Id).ToHashSet();
			foreach (var sound in MediaLinks.SoundsOf(saved.MediaLink).Where(sound => !before.Contains(sound.Id)))
			{
				SoundSync.Upload(saved.Id, sound.Id);
			}
		}

		/// <summary>
		/// A song's media buttons (beside the pencil, and in the editor beside Save): one for each of its
		/// media links (see <see cref="MediaLinks.Split"/>): a recording to listen to or watch, or a sound
		/// file kept on this device (see <see cref="ISoundStore"/>), whose button turns the media player
		/// on or off for it (see <see cref="ToggleMedia"/>): on, it plays and stops with the lyrics (on from
		/// where it stopped, until another song is shown), which follow it (see <see cref="FollowMedia"/>),
		/// and the playback toolbar shows its progress, speed and pitch. Each shows Spotify's or YouTube's icon, a speaker for
		/// a sound file, else a note. A longclick (or right-click) on one shows a menu: remove that link,
		/// and add another: search Spotify or YouTube for the song (its name and artist: the
		/// song's, else the one the lyrics name), paste a link from the clipboard, or pick a sound file. A song
		/// without links has one note button, which shows that menu. Links and sound files can also be
		/// dropped anywhere on the view (see <see cref="OnMediaDropped"/>), and links pasted with
		/// Ctrl/⌘+V (see <see cref="PasteMediaAsync"/>).
		/// </summary>
		/// <param name="getLinks">The media link field now (one link per line).</param>
		/// <param name="setLinks">Sets the media link field (in the editor until Save; beside the pencil, saved at once).</param>
		/// <param name="showProblem">Says what went wrong (e.g. the browser couldn't be opened), at the control.</param>
		/// <returns>
		/// The buttons; what to call when the links were changed elsewhere (to show them); what keeps a
		/// sound file on the device and adds it (for one dropped on the view); and what adds a link.
		/// </returns>
		private (StackPanel Buttons, Action ShowLinks, Func<IStorageFile, Task> AddSound, Func<string, Task> AddLink) MediaButtons(
			Func<string> getLinks, Func<string, Task> setLinks, Func<string> name, Func<string> lyrics, Func<string> artist, Action<Control, string> showProblem)
		{
			var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			var sounds = App.SoundStore;

			// Problems from a drop or a paste are shown at the first button.
			Control Anchor() => buttons.Children.Count > 0 ? buttons.Children[0] : buttons;

			void ShowLinks()
			{
				buttons.Children.Clear();
				var links = MediaLinks.Split(getLinks());
				if (links.Count == 0)
				{
					buttons.Children.Add(EmptyButton());
				}

				foreach (var link in links)
				{
					buttons.Children.Add(LinkButton(link));
				}
			}

			// The speaker turns into a stop sign while its sound plays, whoever started it.
			void OnPlayingChanged() => Dispatcher.UIThread.Post(ShowLinks);
			if (sounds != null)
			{
				buttons.AttachedToVisualTree += (_, _) =>
				{
					sounds.PlayingChanged += OnPlayingChanged;
					ShowLinks();
				};
				buttons.DetachedFromVisualTree += (_, _) => sounds.PlayingChanged -= OnPlayingChanged;
			}

			LongClickButton NewButton() => new() { Padding = new Thickness(8), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

			// No links yet: a note, whose click (or longclick) shows how to add one.
			LongClickButton EmptyButton()
			{
				var button = NewButton();
				button.Content = MediaLinks.Icon(MediaSite.Other);
				const string tip = "Media: search Spotify or YouTube, paste a link or pick a sound file (or drop one here, or paste a link with Ctrl/⌘+V)";
				ToolTip.SetTip(button, tip);
				Avalonia.Automation.AutomationProperties.SetName(button, tip);
				var menu = Menu(button, null);
				button.Click += (_, _) => menu.ShowAt(button);
				button.LongClick += () => menu.ShowAt(button);
				return button;
			}

			LongClickButton LinkButton(string link)
			{
				var button = NewButton();
				var sound = MediaLinks.SoundOf(link);
				var uri = MediaLinks.Parse(link);
				var site = sound != null ? MediaSite.Sound : MediaLinks.SiteOf(uri);
				var playing = sound != null && sounds?.Playing == sound.Id;
				var active = sound != null && ActiveSound?.Id == sound.Id;
				button.Content = MediaLinks.Icon(site, playing);
				// The sound the media player is on for: filled with the highlight colour.
				if (active)
				{
					button.Bind(BackgroundProperty, button.GetResourceObservable("AccentButtonBackground"));
				}
				// Played slower or faster, or in another key: said, so it's no surprise.
				var tuned = sounds != null && (Math.Abs(sounds.Speed - 1) > 0.001 || Math.Abs(sounds.Pitch) > 0.001)
					? $" at {SoundTuning.DescribeSpeed(sounds.Speed)}, {SoundTuning.DescribePitch(sounds.Pitch).ToLowerInvariant()}"
					: "";
				var tip = sound != null
					? active
						? $"Media player on for {sound.Name}{tuned}: it plays and stops with the lyrics. Click to turn off (hold for more)"
						: $"Turn the media player on for {sound.Name}: it plays and stops with the lyrics (hold for more)"
					: site switch
					{
						MediaSite.Spotify => "Play on Spotify (hold for more)",
						MediaSite.YouTube => "Play on YouTube (hold for more)",
						_ => $"Open {link} (hold for more)",
					};
				ToolTip.SetTip(button, tip);
				Avalonia.Automation.AutomationProperties.SetName(button, tip);

				var menu = Menu(button, link);
				button.LongClick += () => menu.ShowAt(button);
				button.Click += (_, _) =>
				{
					if (sound != null)
					{
						ToggleMedia(sound);
					}
					else if (uri != null)
					{
						// A Spotify or YouTube song in its single-song player, under this button (the app's player, which
						// plays on over other pages); anything else (an album, another site) opens outside.
						var embed = MediaLinks.PlayerFor(uri);
						if (MediaPlayerLayer.Current is { IsShown: true } shown && shown.Source == embed)
						{
							// Pressed again while its player shows: it goes out of sight, the song plays on.
							shown.Hide();
						}
						else if (embed != uri && MediaPlayerLayer.Current is { } layer)
						{
							layer.Show(embed, button, song.Id);
						}
						else
						{
							_ = OpenAsync(button, uri);
						}
					}
					else
					{
						showProblem(button, $"\"{link}\" isn't a link that can be opened. Hold the button to remove it.");
					}
				};
				return button;
			}

			async Task OpenAsync(Control button, Uri uri)
			{
				if (TopLevel.GetTopLevel(this) is not { } topLevel || !await topLevel.Launcher.LaunchUriAsync(uri))
				{
					showProblem(button, $"Couldn't open {uri}.");
				}
			}

			async Task ChangeAsync(Control at, string field)
			{
				try
				{
					await setLinks(field);
				}
				catch (Exception ex)
				{
					showProblem(at, $"Couldn't save the media links. {ex.Message}");
				}

				ShowLinks();
			}

			async Task AddLinkAsync(Control at, string link)
			{
				if (MediaLinks.With(getLinks(), link, MaxMediaLinkLength, out var problem) is { } field)
				{
					await ChangeAsync(at, field);
				}
				else
				{
					showProblem(at, problem!);
				}
			}

			async Task PasteAsync(Control at)
			{
				var text = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
					? await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard)
					: null;
				if (!MediaLinks.IsMediaLink(text))
				{
					showProblem(at, string.IsNullOrWhiteSpace(text)
						? "There's no link on the clipboard to paste."
						: "What's on the clipboard isn't a link (a Spotify or YouTube link, or another web address).");
					return;
				}

				await AddLinkAsync(at, text!.Trim());
			}

			async Task AddSoundAsync(Control at, IStorageFile file)
			{
				if (sounds == null)
				{
					showProblem(at, "Sound files can't be kept here.");
					return;
				}

				if (!MediaLinks.IsSoundFile(file.Name))
				{
					showProblem(at, $"{file.Name} isn't a sound file this app keeps ({string.Join(", ", MediaLinks.SoundExtensions)}).");
					return;
				}

				try
				{
					var sound = new SoundFile(MediaLinks.NewSoundId(file.Name), file.Name);
					await using (var content = await file.OpenReadAsync())
					{
						if (content.CanSeek && content.Length > MediaLinks.MaxSoundSize)
						{
							showProblem(at, $"{file.Name} is too big: a sound file can be at most {MediaLinks.MaxSoundSize / (1024 * 1024)} MB.");
							return;
						}

						await sounds.SaveAsync(sound.Id, content);
					}

					await AddLinkAsync(at, MediaLinks.SoundLink(sound, MaxMediaLinkLength));
				}
				catch (Exception ex)
				{
					showProblem(at, $"Couldn't keep {file.Name}. {ex.Message}");
				}
			}

			async Task PickSoundAsync(Control at)
			{
				if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
				{
					showProblem(at, "Files can't be picked here.");
					return;
				}

				var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Pick sound files for the song",
					AllowMultiple = true,
					FileTypeFilter =
					[
						new FilePickerFileType("Sound files")
						{
							Patterns = MediaLinks.SoundExtensions.Select(extension => "*" + extension).ToArray(),
							MimeTypes = ["audio/*"],
						},
					],
				});
				foreach (var file in files)
				{
					await AddSoundAsync(at, file);
				}
			}

			MenuItem Item(string header, Func<Task> action)
			{
				var item = new MenuItem { Header = header };
				item.Click += (_, _) => _ = action();
				return item;
			}

			// One link's menu (or, without a link, adding one): its own items first, then adding another.
			MenuFlyout Menu(LongClickButton button, string? link)
			{
				var menu = new MenuFlyout();
				if (link != null)
				{
					menu.Items.Add(Item(MediaLinks.SoundOf(link) is { } sound ? $"Remove {sound.Name}" : "Remove this link",
						() => ChangeAsync(button, MediaLinks.Without(getLinks(), link))));
					menu.Items.Add(new Separator());
				}

				var another = link == null ? "" : " another";
				menu.Items.Add(Item("Search on Spotify", () => OpenAsync(button, MediaLinks.Search(MediaSite.Spotify, MediaLinks.Query(name(), lyrics(), artist())))));
				menu.Items.Add(Item("Search on YouTube", () => OpenAsync(button, MediaLinks.Search(MediaSite.YouTube, MediaLinks.Query(name(), lyrics(), artist())))));
				menu.Items.Add(Item($"Paste{another} link from clipboard", () => PasteAsync(button)));
				if (sounds != null)
				{
					menu.Items.Add(Item($"Pick{another} sound file…", () => PickSoundAsync(button)));
				}

				return menu;
			}

			ShowLinks();
			return (buttons, ShowLinks, file => AddSoundAsync(Anchor(), file), link => AddLinkAsync(Anchor(), link));
		}

		/// <summary>
		/// Ctrl/⌘+V on the lyrics (not while editing): a link on the clipboard (Spotify, YouTube or
		/// another web address) is added to the song's media links, as are sound files copied to it.
		/// Anything else is left alone; false then, so the key can do its usual thing.
		/// </summary>
		public async Task<bool> PasteMediaAsync()
		{
			if (IsEditing || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
			{
				return false;
			}

			try
			{
				var files = await Avalonia.Input.Platform.ClipboardExtensions.TryGetFilesAsync(clipboard);
				var soundFiles = files?.OfType<IStorageFile>().Where(file => MediaLinks.IsSoundFile(file.Name)).ToList() ?? [];
				if (soundFiles.Count > 0)
				{
					foreach (var file in soundFiles)
					{
						await addSoundBesidePencil(file);
					}

					return true;
				}

				var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
				if (!MediaLinks.IsMediaLink(text))
				{
					return false;
				}

				await addLinkBesidePencil(text!.Trim());
				return true;
			}
			catch (Exception ex)
			{
				// E.g. the browser didn't allow reading the clipboard.
				System.Diagnostics.Debug.WriteLine($"Couldn't paste a media link: {ex.Message}");
				return false;
			}
		}

		// Where the song's sound file was when it stopped (or the view went away), to play on from
		// there; forgotten once another song is shown.
		private static (Guid SongId, string SoundId, TimeSpan Position)? soundResume;

		// While the song's sound file plays, the lyrics follow it (see FollowMedia); scrolled by hand,
		// they're left where they were put for a moment (to look ahead, say), then follow again.
		private const long HandScrollPause = 3000;
		private long handScrolledAt = long.MinValue / 2;

		/// <summary>Remembers where the song's sound file is, if one of its sounds plays now.</summary>
		private void RememberSoundPosition()
		{
			if (App.SoundStore is { Playing: { } playing } sounds && SongSounds().Any(sound => sound.Id == playing) && sounds.Position is { } position)
			{
				soundResume = (song.Id, playing, position);
			}
		}

		/// <summary>The sound files among the song's media links.</summary>
		private IEnumerable<SoundFile> SongSounds() => MediaLinks.SoundsOf(song.MediaLink);

		/// <summary>Whether one of the song's sound files plays now (in the app).</summary>
		private bool SongSoundPlays => App.SoundStore?.Playing is { } playing && SongSounds().Any(sound => sound.Id == playing);

		/// <summary>Plays on from where the sound stopped before (until another song was shown), unless that's the very end.</summary>
		private void PlayOnFromRemembered(SoundFile sound)
		{
			if (App.SoundStore is { } sounds && soundResume is { } resume && resume.SongId == song.Id && resume.SoundId == sound.Id)
			{
				soundResume = null;
				if (sounds.Duration is not { } duration || resume.Position < duration - TimeSpan.FromSeconds(1))
				{
					sounds.Seek(resume.Position);
				}
			}
		}

		// The media player: on (for every song, until turned off) or off. On, a song's sound file plays
		// and stops with its lyrics, and the playback toolbar shows its progress, speed and pitch.
		private static bool mediaOn;

		// The sound file of this song the media player plays: the one picked last, else its first.
		private string? pickedSoundId;

		/// <summary>The song's sound file the media player plays; null when it's off, or the song has none (or sounds can't be played here).</summary>
		public SoundFile? ActiveSound
		{
			get
			{
				if (!mediaOn || App.SoundStore == null)
				{
					return null;
				}

				var songSounds = SongSounds().ToList();
				return songSounds.FirstOrDefault(sound => sound.Id == pickedSoundId) ?? songSounds.FirstOrDefault();
			}
		}

		/// <summary>Raised when the media player is turned on or off, or another sound file is picked.</summary>
		public event Action? MediaChanged;

		/// <summary>
		/// A sound file's button: turns the media player on for that sound, or off when it's on for it
		/// already (stopping it, if it plays). On while the lyrics scroll, the sound starts at once.
		/// </summary>
		private void ToggleMedia(SoundFile sound)
		{
			if (ActiveSound?.Id == sound.Id)
			{
				mediaOn = false;
				StopMedia();
			}
			else
			{
				// Another of the song's sounds playing stops (and the lyrics with it); press play for this one.
				if (SongSoundPlays && App.SoundStore?.Playing != sound.Id)
				{
					StopMedia();
				}

				pickedSoundId = sound.Id;
				mediaOn = true;
				if (IsAutoScrolling)
				{
					_ = StartMediaAsync();
				}
			}

			MediaChanged?.Invoke();
			showMediaLink();
		}

		/// <summary>
		/// Pressing play: starts the song's sound file too, when the media player is on for it (see
		/// <see cref="ActiveSound"/>), on from where it stopped. One added on another device is fetched
		/// from the server first. Nothing when one of its sounds plays already.
		/// </summary>
		public async Task StartMediaAsync()
		{
			if (App.SoundStore is not { } sounds || SongSoundPlays || ActiveSound is not { } sound)
			{
				return;
			}

			try
			{
				if (!await sounds.PlayAsync(sound.Id) && !(await SoundSync.FetchAsync(sound.Id) && await sounds.PlayAsync(sound.Id)))
				{
					return; // Not on this device, nor (yet) on the server.
				}

				startedSound = true;
				PlayOnFromRemembered(sound);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't play {sound.Name}: {ex.Message}");
			}
		}

		/// <summary>
		/// Back to the start: the song's sound file playing goes back to its start (true then); one
		/// stopped plays from its start the next time (where it stopped is forgotten).
		/// </summary>
		private bool RestartMedia()
		{
			if (SongSoundPlays)
			{
				App.SoundStore!.Seek(TimeSpan.Zero);
				return true;
			}

			if (soundResume?.SongId == song.Id)
			{
				soundResume = null;
			}

			return false;
		}

		/// <summary>Pressing stop: stops the song's sound file too, if one plays (remembering where, to play on from there).</summary>
		public void StopMedia()
		{
			if (SongSoundPlays)
			{
				RememberSoundPosition();
				App.SoundStore?.Stop();
			}
		}

		/// <summary>The lyrics were scrolled by hand: they stay there a moment before following the sound file again.</summary>
		private void ScrolledByHand() => handScrolledAt = Environment.TickCount64;

		/// <summary>
		/// While the song's sound file plays, the lyrics go where it is (instead of by the clock): by the
		/// song's timeline when it has time tags (that time at the middle of the view), else by how far
		/// into the sound it is (its start at the top, its end at the bottom). False when no sound of the
		/// song plays (or where it is isn't known); true, without moving, for a moment after scrolling by hand.
		/// </summary>
		private bool FollowMedia(ScrollViewer viewer)
		{
			if (!SongSoundPlays || App.SoundStore is not { Position: { } position } sounds)
			{
				return false;
			}

			if (Environment.TickCount64 - handScrolledAt < HandScrollPause)
			{
				return true;
			}

			double target;
			if (IsTimed && Timeline() is { } points)
			{
				var seconds = position.TotalSeconds;
				ShowCurrentPart(seconds);
				target = CenterAt(points, seconds) - viewer.Viewport.Height / 2;
				timedPoints = points;
			}
			else if (sounds.Duration is { TotalSeconds: > 0 } duration)
			{
				target = position.TotalSeconds / duration.TotalSeconds * MaxOffset(viewer);
			}
			else
			{
				return false;
			}

			target = Math.Round(Math.Clamp(target, 0, MaxOffset(viewer)));
			viewer.Offset = new Vector(viewer.Offset.X, target);
			timedOffset = target;
			return true;
		}

		/// <summary>Raised (true or false) when one of the song's sound files starts or stops playing, so scrolling can start or stop with it.</summary>
		public event Action<bool>? MediaPlayingChanged;

		// Whether a sound of the song played at the last change, so only a start or a stop is told.
		private bool songSoundPlayed;

		private void OnSoundPlayingChanged() => Dispatcher.UIThread.Post(() =>
		{
			var plays = SongSoundPlays;
			if (plays != songSoundPlayed)
			{
				songSoundPlayed = plays;
				MediaPlayingChanged?.Invoke(plays);
			}
		});

		// This view started a sound playing (in the app): it's stopped when the view goes away.
		private bool startedSound;

		// Keep a dropped sound file, and add a dropped link, to the song's media links: the editor's
		// while editing (until Save), else the media buttons beside the pencil's (saved at once).
		private Func<IStorageFile, Task>? addDroppedSound;
		private Func<string, Task>? addDroppedLink;

		/// <summary>Sound files or a link dragged over the view: they can be dropped (anywhere) to be added to the song's media links.</summary>
		private void OnMediaDraggedOver(object? sender, DragEventArgs e)
		{
			if ((addDroppedSound != null && DroppedSounds(e).Count > 0) || (addDroppedLink != null && DroppedLink(e) != null))
			{
				e.DragEffects = DragDropEffects.Copy;
				e.Handled = true;
			}
		}

		private async void OnMediaDropped(object? sender, DragEventArgs e)
		{
			var files = addDroppedSound != null ? DroppedSounds(e) : [];
			var link = addDroppedLink != null && files.Count == 0 ? DroppedLink(e) : null;
			if (files.Count == 0 && link == null)
			{
				return;
			}

			e.Handled = true;
			foreach (var file in files)
			{
				await addDroppedSound!(file);
			}

			if (link != null)
			{
				await addDroppedLink!(link);
			}
		}

		/// <summary>The sound files among what's dragged.</summary>
		private static IReadOnlyList<IStorageFile> DroppedSounds(DragEventArgs e)
			=> e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Where(file => MediaLinks.IsSoundFile(file.Name)).ToList() ?? [];

		/// <summary>A link dragged (e.g. from the browser's address bar, or Spotify's Share); null if there's none.</summary>
		private static string? DroppedLink(DragEventArgs e)
			=> e.DataTransfer.TryGetText() is { } text && MediaLinks.IsMediaLink(text) ? text.Trim() : null;

		/// <summary>A problem beside the lyrics' media button, where there's no room for an error line.</summary>
		private static void ShowProblemAt(Control button, string message)
		{
			var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
			text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(LyriousTheme.ErrorBrush));
			new Flyout { Content = text }.ShowAt(button);
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
			var artist = new TextBox { Text = song.Artist, MaxLength = 100, PlaceholderText = "Artist", MinWidth = 200 };
			var key = Choice(Keys, song.PlayingKey, NoKey);
			var originalKey = Choice(Keys, song.OriginalKey, NoOriginalKey);
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
			var save = PageLayout.OkButton("Save song");
			// Escape cancels too (before the page's ✕).
			var cancel = EscapeKey.MarkAsClose(HomeView.IconButton(CancelIcon, "Cancel editing"), first: true);
			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));
			// What in the MIDI tags can't be sent (with the band's expressions), as the lyrics are typed.
			var midiNotes = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
			midiNotes.Bind(TextBlock.ForegroundProperty, midiNotes.GetResourceObservable(LyriousTheme.WarningBrush));
			void ShowMidiNotes()
			{
				var problems = Midi.Problems(editor.Text, MidiExpressions);
				midiNotes.Text = string.Join("\n", problems.Select(problem => "MIDI: " + problem));
				midiNotes.IsVisible = problems.Count > 0;
			}

			editor.TextChanged += (_, _) => ShowMidiNotes();
			MidiExpressionsChanged = ShowMidiNotes;
			ShowMidiNotes();
			// Changed from the media button's menu, kept until Save like the rest.
			var mediaLink = song.MediaLink;
			var (media, _, addSound, addLink) = MediaButtons(
				() => mediaLink,
				link =>
				{
					mediaLink = link;
					return Task.CompletedTask;
				},
				() => string.IsNullOrWhiteSpace(name.Text) ? song.Name : name.Text,
				() => editor.Text ?? "",
				() => artist.Text ?? "",
				(_, message) =>
				{
					error.Text = message;
					error.IsVisible = true;
				});
			// Sound files and links dropped while editing are added here, until Save.
			addDroppedSound = addSound;
			addDroppedLink = addLink;

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
			originalKey.MinWidth = 0;
			beat.MinWidth = 0;
			tempo.MinWidth = 0;
			tempo.Width = 72;
			tempo.ShowButtonSpinner = false;
			ToolTip.SetTip(key, "Playing key: the key the band plays the song in");
			ToolTip.SetTip(originalKey, "Original key: the key the song was written or recorded in");
			ToolTip.SetTip(tempo, "Tempo (bpm)");
			ToolTip.SetTip(beat, "Beat");
			var originalLabel = new TextBlock { Text = "orig.", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
			var detailsInputs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { key, originalLabel, originalKey, tempo, beat } };

			// The song with the keys, tempo and beat as they are in the inputs.
			SongDetails WithDetails() => song with
			{
				PlayingKey = key.SelectedItem as string is { } k && k != NoKey ? k : "",
				OriginalKey = originalKey.SelectedItem as string is { } o && o != NoOriginalKey ? o : "",
				Beat = beat.SelectedItem as string ?? song.Beat,
				Tempo = (float)(tempo.Value ?? (decimal)song.Tempo),
			};

			var info = PageLayout.SongInfo(song);
			var detailsText = new TextBlock
			{
				Text = string.IsNullOrEmpty(info) ? "Set keys, tempo and beat" : info,
				Opacity = 0.7,
				Background = Brushes.Transparent,
				Cursor = new Cursor(StandardCursorType.Hand),
				Margin = new Thickness(0, 0, 0, 5),
			};
			ToolTip.SetTip(detailsText, "Click to change the playing key, original key, tempo and beat");
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
				if (key.IsDropDownOpen || originalKey.IsDropDownOpen || beat.IsDropDownOpen)
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
					detailsText.Text = string.IsNullOrEmpty(edited) ? "Set keys, tempo and beat" : edited;
					detailsSlot.Content = detailsText;
				}
			});
			detailsInputs.LostFocus += (_, _) => CheckDetailsFocus();
			key.DropDownClosed += (_, _) => CheckDetailsFocus();
			originalKey.DropDownClosed += (_, _) => CheckDetailsFocus();
			beat.DropDownClosed += (_, _) => CheckDetailsFocus();

			// The tags likewise: chips (or "Add tags") until clicked, then a text box of tags separated
			// by commas, until it loses the focus or Enter is pressed.
			var tagsBox = new TextBox
			{
				Text = SongTags.Tidy(song.Tags),
				PlaceholderText = "Tags, separated by spaces",
				MinWidth = 240,
				MaxLength = 500,
			};
			ToolTip.SetTip(tagsBox, "Tags, separated by spaces (e.g. ballad christmas)");
			// The artist likewise: "by …" (or "Add artist") until clicked, then its text box, until it
			// loses the focus or Enter is pressed.
			var artistSlot = new ContentControl { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 5) };
			void ShowArtistText()
			{
				var text = artist.Text?.Trim() ?? "";
				var shown = new TextBlock
				{
					Text = text.Length == 0 ? "Add artist" : $"by {text}",
					Opacity = 0.7,
					FontStyle = text.Length == 0 ? FontStyle.Italic : FontStyle.Normal,
					Background = Brushes.Transparent,
					Cursor = new Cursor(StandardCursorType.Hand),
				};
				ToolTip.SetTip(shown, "Click to change the artist");
				shown.Tapped += (_, e) =>
				{
					e.Handled = true;
					artistSlot.Content = artist;
					Dispatcher.UIThread.Post(() =>
					{
						artist.Focus();
						artist.SelectAll();
					});
				};
				artistSlot.Content = shown;
			}

			artist.LostFocus += (_, _) => ShowArtistText();
			artist.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					editor.Focus();
				}
			};
			ShowArtistText();

			var tagsSlot = new ContentControl { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 3) };

			// Below the box, the songbook's tags that the song doesn't have yet, narrowed to the ones
			// containing what's typed after the last space; clicking one puts it in the box.
			var usedTagsList = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, MaxWidth = 360 };
			var usedTagsFrame = new Border
			{
				Padding = new Thickness(8),
				CornerRadius = new CornerRadius(6),
				BorderThickness = new Thickness(1),
				Child = new StackPanel
				{
					Spacing = 6,
					Children =
					{
						new TextBlock { Text = "Used tags", FontSize = 12, Opacity = 0.7 },
						usedTagsList,
					},
				},
			};
			usedTagsFrame.Bind(Border.BackgroundProperty, usedTagsFrame.GetResourceObservable("FlyoutPresenterBackground"));
			usedTagsFrame.Bind(Border.BorderBrushProperty, usedTagsFrame.GetResourceObservable("FlyoutBorderThemeBrush"));
			var usedTagsPopup = new Avalonia.Controls.Primitives.Popup
			{
				PlacementTarget = tagsBox,
				Placement = PlacementMode.BottomEdgeAlignedLeft,
				VerticalOffset = 4,
				Child = usedTagsFrame,
			};
			var tagsEditor = new Panel { Children = { tagsBox, usedTagsPopup } };
			IReadOnlyList<string>? usedTags = null;
			// Set while a tag in the popup is being clicked, so the box keeps showing.
			var pickingTag = false;

			// The text after the last space (or comma): the tag being typed.
			string TypedTag()
			{
				var text = tagsBox.Text ?? "";
				return text[(text.LastIndexOfAny(SongTags.Separators) + 1)..].Trim().TrimStart('#');
			}

			void PickTag(string tag)
			{
				var text = tagsBox.Text ?? "";
				var kept = text[..(text.LastIndexOfAny(SongTags.Separators) + 1)];
				tagsBox.Text = SongTags.Join(SongTags.Parse(kept).Append(tag)) + " ";
				tagsBox.CaretIndex = tagsBox.Text.Length;
				tagsBox.Focus();
			}

			void ShowUsedTags()
			{
				var typed = TypedTag();
				var have = SongTags.Parse(tagsBox.Text);
				var offered = (usedTags ?? [])
					.Where(tag => !have.Contains(tag, StringComparer.CurrentCultureIgnoreCase) || tag.Equals(typed, StringComparison.CurrentCultureIgnoreCase))
					.Where(tag => typed.Length == 0 || tag.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
					.Where(tag => !tag.Equals(typed, StringComparison.CurrentCultureIgnoreCase))
					.ToArray();
				usedTagsList.Children.Clear();
				foreach (var tag in offered)
				{
					var chip = PageLayout.TagChip(tag, 13);
					chip.Cursor = new Cursor(StandardCursorType.Hand);
					chip.Padding = new Thickness(10, 3);
					ToolTip.SetTip(chip, $"Add {tag}");
					chip.PointerPressed += (_, e) =>
					{
						pickingTag = true;
						e.Handled = true;
					};
					chip.PointerReleased += (_, e) =>
					{
						e.Handled = true;
						PickTag(tag);
						pickingTag = false;
					};
					usedTagsList.Children.Add(chip);
				}

				usedTagsPopup.IsOpen = offered.Length > 0 && tagsSlot.Content == tagsEditor;
			}

			tagsBox.GotFocus += async (_, _) =>
			{
				if (usedTags == null && LoadUsedTags is { } load)
				{
					try
					{
						usedTags = await load();
					}
					catch (Exception ex)
					{
						usedTags = [];
						System.Diagnostics.Debug.WriteLine($"Couldn't get the songbook's tags: {ex.Message}");
					}
				}

				ShowUsedTags();
			};
			tagsBox.TextChanged += (_, _) =>
			{
				if (tagsBox.IsFocused)
				{
					ShowUsedTags();
				}
			};

			void ShowTagsText()
			{
				usedTagsPopup.IsOpen = false;
				var tags = SongTags.Sorted(tagsBox.Text); // As they'll be saved.
				Control shown = tags.Count == 0
					? new TextBlock { Text = "Add tags", Opacity = 0.7, FontStyle = FontStyle.Italic, Margin = new Thickness(0, 0, 0, 2) }
					: PageLayout.TagChips(tags);
				var holder = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = shown };
				ToolTip.SetTip(holder, "Click to change the tags");
				holder.Tapped += (_, e) =>
				{
					e.Handled = true;
					tagsSlot.Content = tagsEditor;
					Dispatcher.UIThread.Post(() =>
					{
						tagsBox.Focus();
						tagsBox.CaretIndex = tagsBox.Text?.Length ?? 0;
					});
				};
				tagsSlot.Content = holder;
			}

			// Checked a moment after the focus left: a tag clicked in the popup brings it back.
			tagsBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
			{
				if (!pickingTag && !tagsBox.IsFocused)
				{
					ShowTagsText();
				}
			});
			tagsBox.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					editor.Focus();
				}
				else if (e.Key == Avalonia.Input.Key.Escape && usedTagsPopup.IsOpen)
				{
					e.Handled = true;
					usedTagsPopup.IsOpen = false;
				}
			};
			ShowTagsText();

			editHeader = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { nameSlot, artistSlot, detailsSlot, tagsSlot } };

			// The media button, Save and Cancel take the pencil's place at the right of the title.
			titleActions.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { media, save, cancel } };
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
					MediaLink = mediaLink.Trim(),
					Tags = SongTags.Tidy(tagsBox.Text),
					Artist = artist.Text?.Trim() ?? "",
				};

				var inputs = new Control[] { save, cancel, media, name, artist, key, originalKey, tempo, beat, tagsBox, editor };
				foreach (var input in inputs)
				{
					input.IsEnabled = false;
				}

				error.IsVisible = false;
				try
				{
					var before = song.MediaLink;
					song = await saveSong(edited);
					UploadNewSound(before, song);
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
				Children = { new StackPanel { Children = { error, midiNotes } }, editor },
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
