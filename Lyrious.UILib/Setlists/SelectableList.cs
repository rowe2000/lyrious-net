using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A list where a click selects a row (outlined, with its small toolbar beside it) and a double
	/// click makes the item current (e.g. opens the song, setlist or band). The current item is shown
	/// with the blue highlight and starts out selected. Used by the bands, setlists and songbook pages.
	/// </summary>
	public sealed class SelectableList<T> : UserControl where T : notnull
	{
		private readonly List<(T Item, Border Outline, Control Tools)> rows = [];

		// Each item's whole row (with its toolbar), to hide it when it isn't found (see Filter).
		private readonly List<(T Item, Control Row)> shownRows = [];

		private readonly bool activateOnClick;
		private readonly Thickness rowPadding;
		private readonly bool toolsBelow;

		/// <summary>The room around a row's content, unless the list is given another.</summary>
		public static readonly Thickness RowPadding = new(16, 12);

		private IDisposable? selectedOutline;

		/// <summary>Lists only the items <paramref name="shown"/> keeps (e.g. those a search finds); the others are hidden.</summary>
		public void Filter(Func<T, bool> shown)
		{
			foreach (var (item, row) in shownRows)
			{
				row.IsVisible = shown(item);
			}
		}

		/// <summary>How many items are listed (those found, while filtered).</summary>
		public int ShownCount => shownRows.Count(pair => pair.Row.IsVisible);

		/// <summary>The first item listed (the first found, while filtered), if any.</summary>
		public T? FirstShown => shownRows.FirstOrDefault(pair => pair.Row.IsVisible).Item;

		/// <summary>The selected item (outlined), if any.</summary>
		public T? Selected { get; private set; }

		/// <summary>Raised when another row is selected (a click).</summary>
		public event Action<T?>? SelectionChanged;

		/// <param name="content">What a row shows for an item.</param>
		/// <param name="isCurrent">Whether the item is the current one (blue highlight).</param>
		/// <param name="tools">The buttons of the toolbar shown beside the selected row.</param>
		/// <param name="activate">Makes the item current (on a double click).</param>
		/// <param name="activateOnClick">A click makes the item current at once (no selecting first), e.g. a setlist's songs.</param>
		/// <param name="rowPadding">The room around a row's content; else <see cref="RowPadding"/>.</param>
		/// <param name="toolsBelow">The selected row's toolbar under it (the row keeps the whole width), not beside it.</param>
		/// <param name="spacing">The room between the rows.</param>
		public SelectableList(
			IReadOnlyList<T> items,
			Func<T, Control> content,
			Func<T, bool> isCurrent,
			Func<T, IEnumerable<Control>> tools,
			Action<T> activate,
			string emptyText,
			bool activateOnClick = false,
			Thickness? rowPadding = null,
			bool toolsBelow = false,
			double spacing = 4)
		{
			this.activateOnClick = activateOnClick;
			this.rowPadding = rowPadding ?? RowPadding;
			this.toolsBelow = toolsBelow;
			if (items.Count == 0)
			{
				Content = new TextBlock { Text = emptyText, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
				return;
			}

			var list = new StackPanel { Spacing = spacing };
			Control? currentRow = null;
			foreach (var item in items)
			{
				var current = isCurrent(item);
				var row = Row(item, content(item), current, tools(item), activate);
				shownRows.Add((item, row));
				currentRow ??= current ? row : null;
				list.Children.Add(row);
			}

			var scroller = new ScrollViewer { Content = list };
			Content = scroller;
			Select(items.FirstOrDefault(isCurrent));
			if (currentRow != null)
			{
				ScrollToMiddleOnce(scroller, list, currentRow);
			}
		}

		/// <summary>
		/// Once the list is first laid out, scrolls it so the row is in the middle (as far as it can),
		/// so the current item is in sight when the page opens.
		/// </summary>
		private static void ScrollToMiddleOnce(ScrollViewer scroller, Control list, Control row)
		{
			void OnLayout(object? sender, EventArgs e)
			{
				if (scroller.Viewport.Height <= 0 || row.Bounds.Height <= 0)
				{
					return; // Not laid out yet.
				}

				scroller.LayoutUpdated -= OnLayout;
				if (row.TranslatePoint(default, list) is { } top)
				{
					var offset = top.Y - (scroller.Viewport.Height - row.Bounds.Height) / 2;
					scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(offset, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
				}
			}

			scroller.LayoutUpdated += OnLayout;
		}

		private Control Row(T item, Control content, bool isCurrent, IEnumerable<Control> tools, Action<T> activate)
		{
			var button = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = rowPadding,
			};
			if (isCurrent)
			{
				button.Classes.Add("accent");
			}

			if (activateOnClick)
			{
				button.Click += (_, _) => activate(item);
			}
			else
			{
				button.Click += (_, _) => Select(item);
				// The button handles the pointer itself, so listen to handled double taps too.
				button.AddHandler(DoubleTappedEvent, (_, e) =>
				{
					e.Handled = true;
					activate(item);
				}, RoutingStrategies.Bubble, handledEventsToo: true);
			}

			var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
			toolbar.Children.AddRange(tools);
			DockPanel.SetDock(toolbar, Dock.Right);

			// The selection outline is drawn around the button, so the button's own hover and
			// pressed styles don't hide it. Unselected rows have a transparent one of the same size.
			var outline = new Border
			{
				BorderThickness = new Thickness(2),
				BorderBrush = Brushes.Transparent,
				CornerRadius = new CornerRadius(6),
				Child = button,
			};

			rows.Add((item, outline, toolbar));
			if (toolsBelow)
			{
				// Under the row, at its right, a little apart.
				toolbar.Margin = new Thickness(0, 4, 0, 2);
				toolbar.HorizontalAlignment = HorizontalAlignment.Right;
				return new StackPanel { Children = { outline, toolbar } };
			}

			return new DockPanel { Children = { toolbar, outline } };
		}

		/// <summary>The selected row's toolbar hidden (it stays outlined), e.g. while songs are checked for the setlist tool.</summary>
		public bool ToolsHidden
		{
			get => toolsHidden;
			set
			{
				if (toolsHidden != value)
				{
					toolsHidden = value;
					Select(Selected);
				}
			}
		}

		private bool toolsHidden;

		/// <summary>Selects the item (or nothing): outlines its row and shows its toolbar (unless <see cref="ToolsHidden"/>).</summary>
		public void Select(T? item)
		{
			var changed = !EqualityComparer<T?>.Default.Equals(Selected, item);
			Selected = item;
			// Only the selected row's outline follows the selection colour, so the others stay
			// plain when the colours (resources) change.
			selectedOutline?.Dispose();
			selectedOutline = null;
			foreach (var (rowItem, outline, tools) in rows)
			{
				var selected = item is not null && EqualityComparer<T>.Default.Equals(rowItem, item);
				// A row without tools (e.g. the joined band) shows no toolbar, so it doesn't narrow.
				tools.IsVisible = selected && !toolsHidden && tools is Panel { Children.Count: > 0 };
				if (selected)
				{
					selectedOutline = outline.Bind(Border.BorderBrushProperty, outline.GetResourceObservable(LyriousTheme.SelectionBrush));
				}
				else
				{
					outline.BorderBrush = Brushes.Transparent;
				}
			}

			if (changed)
			{
				SelectionChanged?.Invoke(item);
			}
		}
	}
}
