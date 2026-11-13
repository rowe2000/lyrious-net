using System;
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

		/// <summary>How fast the lyrics scroll by themselves, in pixels per second.</summary>
		public double ScrollSpeed { get; set; } = 12;

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
				scroller.Offset = new Vector(scroller.Offset.X, 0);
				scrollRemainder = 0;
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

			if (on)
			{
				// Starting at the end begins again from the top.
				if (scroller!.Offset.Y >= MaxOffset(scroller) - 1)
				{
					scroller.Offset = new Vector(scroller.Offset.X, 0);
				}

				scrollRemainder = 0;
				scrollClock.Restart();
				scrollTimer.Start();
			}
			else
			{
				scrollTimer.Stop();
				scrollClock.Reset();
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

			var seconds = scrollClock.Elapsed.TotalSeconds;
			scrollClock.Restart();
			if (scroller.Extent.Height <= 0)
			{
				return; // Not laid out yet (just opened); the end isn't known.
			}

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

		/// <summary>Whether the lyrics are scrolled all the way to the bottom (or fit without scrolling).</summary>
		public bool IsAtEnd => scroller is { } viewer && viewer.Offset.Y >= MaxOffset(viewer) - 1;

		private static double MaxOffset(ScrollViewer viewer) => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			scrollTimer.Stop();
		}

		private void ShowLyrics()
		{
			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);
			var scroller = new ScrollViewer
			{
				// Transparent, so a tap anywhere in the lyrics area counts, not only on the letters.
				Background = Brushes.Transparent,
				Content = new TextBlock
				{
					Text = hasLyrics ? song.Lyrics : "There are no lyrics for this song yet. Add them with the pencil button.",
					FontSize = hasLyrics ? 24 : 16,
					LineHeight = hasLyrics ? 36 : double.NaN,
					Opacity = hasLyrics ? 1 : 0.7,
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(0, 0, 0, 48),
				},
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
			var cancel = HomeView.IconButton(CancelIcon, "Cancel editing");
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
