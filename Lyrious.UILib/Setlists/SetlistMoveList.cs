using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// "Move songs" mode of a setlist: its songs look as they do in the setlist, with a move symbol (↕)
	/// in place of the check box, and are dragged where they should go (with the mouse anywhere on a
	/// song, by touch by the move symbol, so a finger elsewhere still scrolls); the others make room as
	/// it goes, and the list scrolls when it's dragged near its top or bottom. When songs are checked
	/// (see <see cref="SongsView.CheckableRow"/>), only they can be dragged, and dragging one moves all
	/// of them together, in their order, to where it's dropped; the others show no move symbol. A ✓ in the setlist's
	/// header (see <see cref="DoneButton"/>, in place of its other tools) ends it. Opened by a longclick
	/// on a song in the setlist (see <see cref="OnLongClick"/>). Each move is saved at once; the song
	/// moved last is outlined.
	/// </summary>
	public sealed class SetlistMoveList : UserControl
	{
		// ↕: a line with an arrow head at each end.
		private const string GripIcon = "M12,4 V20 M8,8 L12,4 L16,8 M8,16 L12,20 L16,16";
		// As in the setlist (see SongsView.SongListSpacing).
		private const double Spacing = SongsView.SongListSpacing;
		// How near the top or bottom of the view a dragged song makes the list scroll, and how far per tick.
		private const double ScrollEdge = 48;
		private const double ScrollStep = 14;

		private readonly List<SetlistEntry> order;
		private readonly Guid? currentEntryId;
		private readonly StackPanel rows = new() { Spacing = Spacing };
		private Guid? movedId;

		// The drag going on: the song's row and where it was, where it would go now, where in the row it
		// was taken, the rows' tops and heights when it started, and where the pointer is now.
		private Control? dragged;
		private int dragFrom;
		// The songs dragged (the one taken and the checked ones, by their places, in order), and how
		// many of the others they'd go after.
		private int[] group = [];
		private int dropAfter;
		// Whether the pointer moved the songs at all (a press and let go without moving changes nothing).
		private bool dragMoved;
		private double grabOffset;
		private double[] tops = [];
		private double[] heights = [];
		private Point? pointerInView;
		private ScrollViewer? scrollView;
		private readonly DispatcherTimer edgeScroll = new() { Interval = TimeSpan.FromMilliseconds(30) };

		/// <summary>How to move the songs, e.g. "Drag the songs where you want them.", for the setlist's line under its name while they're moved.</summary>
		public string Hint { get; }

		/// <summary>Raised with the setlist's songs in their new order after each move.</summary>
		public event Action<IReadOnlyList<SetlistEntry>>? Moved;

		/// <param name="pressed">The song that was longclicked: outlined and in view to start with.</param>
		/// <param name="currentEntryId">The band's current song, highlighted as in the setlist.</param>
		public SetlistMoveList(IReadOnlyList<SetlistEntry> entries, Guid? pressed, Guid? currentEntryId = null)
		{
			order = entries.ToList();
			this.currentEntryId = currentEntryId;
			movedId = pressed;

			// With songs checked, they go together (their rows are marked, see Row).
			var checkedCount = order.Count(entry => SongsView.IsEntryChecked(entry.Id));
			Hint = checkedCount > 0
				? L.Plural(checkedCount, "Drag the checked song where you want it.", "Drag all {0} checked songs together")
				: L.T("Drag the songs where you want them.");

			Content = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			edgeScroll.Tick += (_, _) => ScrollAtEdge();
			DetachedFromVisualTree += (_, _) => edgeScroll.Stop();
			ShowRows();

			// The longclicked song in view to start with.
			if (rows.Children.FirstOrDefault(row => row.Tag is Guid id && id == pressed) is { } start)
			{
				Dispatcher.UIThread.Post(() => start.BringIntoView(), DispatcherPriority.Background);
			}
		}

		/// <summary>
		/// The ✓ that ends moving songs, for the setlist's header; Escape presses it too. When the list
		/// goes away first (the page is left or closed), <paramref name="done"/> is done all the same, as
		/// if it was pressed; it's done once either way.
		/// </summary>
		public Button DoneButton(Action done)
		{
			var finished = false;
			void Finish()
			{
				if (!finished)
				{
					finished = true;
					done();
				}
			}

			var button = EscapeKey.MarkAsClose(PageLayout.OkButton(L.T("Done moving songs")));
			button.VerticalAlignment = VerticalAlignment.Center;
			button.Click += (_, _) => Finish();
			DetachedFromVisualTree += (_, _) => Finish();
			return button;
		}

		/// <summary>
		/// Pulses the border (e.g. the setlist's) in the accent colour, to show songs are being moved,
		/// until the returned handle is disposed; the border is as it was then.
		/// </summary>
		public static IDisposable FlashBorder(Border border)
		{
			var accent = border.TryFindResource("AccentButtonBackground", border.ActualThemeVariant, out var found) && found is ISolidColorBrush accentBrush
				? accentBrush.Color
				: Colors.DodgerBlue;
			// Over the border's own colour (as an animation would be), so it's back as it was when this stops.
			var pulsing = new SolidColorBrush(accent);
			var pulse = new BrushPulse(pulsing);
			var binding = border.Bind(Border.BorderBrushProperty, pulse, Avalonia.Data.BindingPriority.Animation);
			var started = DateTime.UtcNow;
			var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
			timer.Tick += (_, _) =>
			{
				// Between full and faint, once every 1.4 seconds.
				var phase = (DateTime.UtcNow - started).TotalSeconds / 1.4 * 2 * Math.PI;
				pulsing.Opacity = 0.15 + 0.85 * (0.5 + 0.5 * Math.Cos(phase));
			};
			timer.Start();
			return new Stopper(() =>
			{
				timer.Stop();
				binding.Dispose();
			});
		}

		/// <summary>The pulsing brush as a value to bind to (it's the same brush throughout; its opacity changes).</summary>
		private sealed class BrushPulse(IBrush brush) : IObservable<object?>
		{
			public IDisposable Subscribe(IObserver<object?> observer)
			{
				observer.OnNext(brush);
				return new Stopper(() => { });
			}
		}

		/// <summary>Does the action when disposed, once.</summary>
		private sealed class Stopper(Action stop) : IDisposable
		{
			private Action? stop = stop;

			public void Dispose()
			{
				stop?.Invoke();
				stop = null;
			}
		}

		/// <summary>The finger (or mouse button) still held down after a longclick, and where it is in the window.</summary>
		public sealed record HeldPointer(IPointer Pointer, Point InWindow);

		/// <summary>
		/// Calls <paramref name="longClick"/> when the control is pressed and held for a second (mouse or
		/// touch; moving more than a little first doesn't count, that's scrolling), with the pointer still
		/// held (so a drag can go on from there, see <see cref="ContinueDrag"/>), or right-clicked (no
		/// pointer held). On a list row's content it's the row's button that's listened to (once it's in
		/// the list): the button takes the whole row's presses, also where the content has nothing to
		/// hit, and every release.
		/// </summary>
		public static void OnLongClick(Control control, Action<HeldPointer?> longClick)
		{
			var hooked = false;
			control.AttachedToVisualTree += (_, _) =>
			{
				if (!hooked)
				{
					hooked = true;
					ListenForLongClick(control.FindAncestorOfType<Button>() ?? control, longClick);
				}
			};
		}

		private static void ListenForLongClick(Control control, Action<HeldPointer?> longClick)
		{
			var timer = new DispatcherTimer { Interval = LongClickToggleButton.Duration };
			Point start = default;
			IPointer? pointer = null;
			Point inWindow = default;
			// A finger wobbles a little while held still: more room for it than for a mouse.
			double slack = 5;
			timer.Tick += (_, _) =>
			{
				timer.Stop();
				longClick(pointer == null ? null : new HeldPointer(pointer, inWindow));
			};
			control.AddHandler(PointerPressedEvent, (_, e) =>
			{
				var point = e.GetCurrentPoint(control);
				if (point.Properties.IsRightButtonPressed)
				{
					e.Handled = true;
					longClick(null);
				}
				else if (point.Properties.IsLeftButtonPressed)
				{
					start = point.Position;
					pointer = e.Pointer;
					slack = e.Pointer.Type == PointerType.Touch ? 10 : 5;
					inWindow = TopLevel.GetTopLevel(control) is { } window ? e.GetPosition(window) : default;
					timer.Start();
				}
			}, RoutingStrategies.Tunnel, handledEventsToo: true);
			control.AddHandler(PointerMovedEvent, (_, e) =>
			{
				if (timer.IsEnabled)
				{
					var at = e.GetPosition(control);
					if (Math.Abs(at.X - start.X) > slack || Math.Abs(at.Y - start.Y) > slack)
					{
						timer.Stop();
					}
					else if (TopLevel.GetTopLevel(control) is { } window)
					{
						inWindow = e.GetPosition(window);
					}
				}
			}, RoutingStrategies.Tunnel, handledEventsToo: true);
			control.AddHandler(PointerReleasedEvent, (_, _) => timer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
			control.AddHandler(PointerCaptureLostEvent, (_, _) => timer.Stop(), RoutingStrategies.Bubble, handledEventsToo: true);
			control.DetachedFromVisualTree += (_, _) => timer.Stop();
		}

		/// <summary>
		/// Goes on with the longclick that opened move mode as a drag of that song, while the finger (or
		/// mouse button) is still held: lift it to drop the song. Called once the list is shown in its
		/// place (it's laid out here). Nothing when the song can't be moved (others are checked).
		/// </summary>
		public void ContinueDrag(HeldPointer held, Guid entryId)
		{
			UpdateLayout();
			if (TopLevel.GetTopLevel(this) is not { } window
			    || rows.Children.FirstOrDefault(row => row.Tag is Guid id && id == entryId) is not Control card
			    || order.FirstOrDefault(entry => entry.Id == entryId) is not { } entry
			    || (order.Any(other => SongsView.IsEntryChecked(other.Id)) && !SongsView.IsEntryChecked(entry.Id))
			    || window.TranslatePoint(held.InWindow, rows) is not { } inRows)
			{
				return;
			}

			held.Pointer.Capture(card);
			StartDrag(card, inRows.Y);
			if (scrollView != null && window.TranslatePoint(held.InWindow, scrollView) is { } inView)
			{
				UpdatePointer(inView);
			}
		}

		private void ShowRows()
		{
			rows.Children.Clear();
			foreach (var entry in order)
			{
				rows.Children.Add(Row(entry));
			}
		}

		/// <summary>One song: its row as in the setlist, with the move symbol in place of the check box.</summary>
		private Control Row(SetlistEntry entry)
		{
			var gripIcon = new Path
			{
				Data = Geometry.Parse(GripIcon),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 20,
				Height = 20,
				Stretch = Stretch.Uniform,
			};
			// In the row's text colour, as the theme gives it (also on the highlighted current song).
			gripIcon.Bind(Shape.StrokeProperty, gripIcon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			// Where the check box is in the setlist (see SongsView.CheckableRow): a little into the row's left padding.
			var grip = new Border
			{
				Child = gripIcon,
				Margin = new Thickness(-6, -6, 10, -6),
				VerticalAlignment = VerticalAlignment.Center,
			};
			ToolTip.SetTip(grip, L.T("Drag to move {0}", entry.Song.Name));
			DockPanel.SetDock(grip, Dock.Left);
			// With songs checked, only they move: the others show no move symbol (its place kept, so
			// the names line up) and can't be taken. The move symbol is only that: it never checks or
			// unchecks a song.
			var movable = !order.Any(other => SongsView.IsEntryChecked(other.Id)) || SongsView.IsEntryChecked(entry.Id);
			if (!movable)
			{
				gripIcon.Opacity = 0;
				ToolTip.SetTip(grip, null);
			}

			// The row as the setlist shows it: a list button (not pressable here; the drag is the card's).
			var face = new Button
			{
				Content = new DockPanel { Children = { grip, SongsView.SongRow(entry.Song, null, entry.Id == currentEntryId, SongsView.SongListExtraHeight) } },
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = SongsView.SongListRowPadding,
				IsHitTestVisible = false,
			};
			if (entry.Id == currentEntryId)
			{
				face.Classes.Add("accent");
			}

			// Its outline, as the setlist's selection outline: the song moved last, or a checked one.
			var card = new Border
			{
				Tag = entry.Id,
				CornerRadius = new CornerRadius(6),
				BorderThickness = new Thickness(2),
				BorderBrush = Brushes.Transparent,
				Background = Brushes.Transparent,
				Cursor = movable ? new Cursor(StandardCursorType.SizeNorthSouth) : Cursor.Default,
				RenderTransform = new TranslateTransform(),
				Child = face,
			};
			if (entry.Id == movedId)
			{
				card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("AccentButtonBackground"));
			}
			else if (SongsView.IsEntryChecked(entry.Id))
			{
				// Checked: it goes with whichever song is dragged.
				card.Bind(Border.BorderBrushProperty, card.GetResourceObservable(LyriousTheme.SelectionBrush));
			}

			card.PointerPressed += (_, e) =>
			{
				// A finger only by the grip, so it can scroll the list elsewhere; the mouse anywhere.
				var point = e.GetCurrentPoint(card);
				var byTouch = e.Pointer.Type == PointerType.Touch;
				// The move symbol isn't hit itself (the row's face isn't), so it's found by where the finger is,
				// with a little room around it for a finger.
				var onGrip = e.GetPosition(grip) is var at && at.X >= -12 && at.X <= grip.Bounds.Width + 12 && at.Y >= -12 && at.Y <= grip.Bounds.Height + 12;
				if (!movable || !point.Properties.IsLeftButtonPressed || (byTouch && !onGrip))
				{
					return;
				}

				e.Handled = true;
				e.PreventGestureRecognition();
				e.Pointer.Capture(card);
				StartDrag(card, e.GetPosition(rows).Y);
			};
			card.PointerMoved += (_, e) =>
			{
				if (dragged == card)
				{
					e.PreventGestureRecognition();
					UpdatePointer(e.GetPosition(scrollView ?? (Visual)rows));
					DragTo(e.GetPosition(rows).Y);
				}
			};
			card.PointerReleased += (_, _) => EndDrag(card);
			card.PointerCaptureLost += (_, _) => EndDrag(card);
			return card;
		}

		private void StartDrag(Control row, double y)
		{
			dragFrom = rows.Children.IndexOf(row);
			if (dragFrom < 0)
			{
				return;
			}

			dragged = row;
			heights = rows.Children.Select(child => child.Bounds.Height).ToArray();
			tops = new double[heights.Length];
			for (var i = 1; i < tops.Length; i++)
			{
				tops[i] = tops[i - 1] + heights[i - 1] + Spacing;
			}

			grabOffset = y - tops[dragFrom];
			dragMoved = false;
			group = Enumerable.Range(0, order.Count).Where(i => i == dragFrom || SongsView.IsEntryChecked(order[i].Id)).ToArray();
			dropAfter = Enumerable.Range(0, dragFrom).Count(i => !group.Contains(i));
			// The dragged songs above the others while they go (solid, so a song they pass over doesn't show through).
			foreach (var i in group)
			{
				var dragging = (Border)rows.Children[i];
				dragging.ZIndex = 1;
				// The list button is see-through in the theme: a solid card under it while it goes.
				dragging.Bind(Border.BackgroundProperty, dragging.GetResourceObservable(LyriousTheme.CardBrush));
			}

			scrollView = ScrollingAncestor();
			edgeScroll.Start();
		}

		/// <summary>
		/// The dragged songs follow the pointer together, one under the other (the taken one where the
		/// pointer has it); the other songs close up and make room for them where they'd go.
		/// </summary>
		private void DragTo(double y)
		{
			if (dragged == null)
			{
				return;
			}

			dragMoved = true;
			// The block of dragged songs: how high it is, and where the taken one is in it.
			var blockHeight = group.Sum(i => heights[i]) + Spacing * (group.Length - 1);
			var takenAt = group.TakeWhile(i => i != dragFrom).Sum(i => heights[i] + Spacing);
			var last = tops.Length - 1;
			// It can go half past the first and last songs, so its middle gets above the first song's (it
			// goes in first) or below the last one's, however tall the block or those songs are.
			var listHeight = tops[last] + heights[last];
			var blockTop = Math.Clamp(y - grabOffset - takenAt, -blockHeight / 2, Math.Max(-blockHeight / 2, listHeight - blockHeight / 2));

			// Where it goes: after every other song whose middle is above the block's middle.
			var middle = blockTop + blockHeight / 2;
			var others = Enumerable.Range(0, order.Count).Where(i => !group.Contains(i)).ToList();
			dropAfter = others.Count(i => tops[i] + heights[i] / 2 < middle);

			// The others, in order, with room for the block after the first dropAfter of them.
			var at = 0.0;
			for (var k = 0; k < others.Count; k++)
			{
				if (k == dropAfter)
				{
					at += blockHeight + Spacing;
				}

				var i = others[k];
				((TranslateTransform)rows.Children[i].RenderTransform!).Y = at - tops[i];
				at += heights[i] + Spacing;
			}

			// The block itself under the pointer.
			var inBlock = 0.0;
			foreach (var i in group)
			{
				((TranslateTransform)rows.Children[i].RenderTransform!).Y = blockTop + inBlock - tops[i];
				inBlock += heights[i] + Spacing;
			}
		}

		/// <summary>Let go: the song goes to its new place (saved), or back where it was.</summary>
		private void EndDrag(Control row)
		{
			if (dragged != row)
			{
				return;
			}

			dragged = null;
			pointerInView = null;
			edgeScroll.Stop();
			if (!dragMoved)
			{
				ShowRows();
				return;
			}

			var entry = order[dragFrom];
			// The dragged songs (in their order) go in after the first dropAfter of the others.
			var moving = group.Select(i => order[i]).ToList();
			var rest = order.Where((_, i) => !group.Contains(i)).ToList();
			rest.InsertRange(Math.Clamp(dropAfter, 0, rest.Count), moving);
			if (rest.SequenceEqual(order))
			{
				// Back where it was: nothing to save.
				ShowRows();
				return;
			}

			order.Clear();
			order.AddRange(rest);
			movedId = entry.Id;
			ShowRows();
			Moved?.Invoke(order.ToList());
		}

		private void UpdatePointer(Point inView) => pointerInView = inView;

		/// <summary>While a song is dragged near the top or bottom of the view, the list scrolls that way (and the song with it).</summary>
		private void ScrollAtEdge()
		{
			if (dragged == null || scrollView == null || pointerInView is not { } at)
			{
				return;
			}

			var step = at.Y < ScrollEdge ? -ScrollStep : at.Y > scrollView.Bounds.Height - ScrollEdge ? ScrollStep : 0;
			var max = Math.Max(0, scrollView.Extent.Height - scrollView.Viewport.Height);
			var offset = Math.Clamp(scrollView.Offset.Y + step, 0, max);
			if (step == 0 || Math.Abs(offset - scrollView.Offset.Y) < 0.5)
			{
				return;
			}

			scrollView.Offset = scrollView.Offset.WithY(offset);
			scrollView.UpdateLayout();
			if (scrollView.TranslatePoint(at, rows) is { } inRows)
			{
				DragTo(inRows.Y);
			}
		}

		/// <summary>The view the list scrolls in: its own, or the page's when the list is in that (and isn't scrolled itself).</summary>
		private ScrollViewer? ScrollingAncestor()
		{
			foreach (var view in rows.GetVisualAncestors().OfType<ScrollViewer>())
			{
				if (view.Extent.Height > view.Viewport.Height + 0.5)
				{
					return view;
				}
			}

			return rows.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
		}
	}
}
