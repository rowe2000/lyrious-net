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

		/// <param name="content">What a row shows for an item.</param>
		/// <param name="isCurrent">Whether the item is the current one (blue highlight).</param>
		/// <param name="tools">The buttons of the toolbar shown beside the selected row.</param>
		/// <param name="activate">Makes the item current (on a double click).</param>
		public SelectableList(
			IReadOnlyList<T> items,
			Func<T, Control> content,
			Func<T, bool> isCurrent,
			Func<T, IEnumerable<Control>> tools,
			Action<T> activate,
			string emptyText)
		{
			if (items.Count == 0)
			{
				Content = new TextBlock { Text = emptyText, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
				return;
			}

			var list = new StackPanel { Spacing = 8 };
			foreach (var item in items)
			{
				list.Children.Add(Row(item, content(item), isCurrent(item), tools(item), activate));
			}

			Content = new ScrollViewer { Content = list };
			Select(items.FirstOrDefault(isCurrent));
		}

		private Control Row(T item, Control content, bool isCurrent, IEnumerable<Control> tools, Action<T> activate)
		{
			var button = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 12),
			};
			if (isCurrent)
			{
				button.Classes.Add("accent");
			}

			button.Click += (_, _) => Select(item);
			// The button handles the pointer itself, so listen to handled double taps too.
			button.AddHandler(DoubleTappedEvent, (_, e) =>
			{
				e.Handled = true;
				activate(item);
			}, RoutingStrategies.Bubble, handledEventsToo: true);

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
			return new DockPanel { Children = { toolbar, outline } };
		}

		/// <summary>Selects the item (or nothing): outlines its row and shows its toolbar.</summary>
		private void Select(T? item)
		{
			foreach (var (rowItem, outline, tools) in rows)
			{
				var selected = item is not null && EqualityComparer<T>.Default.Equals(rowItem, item);
				tools.IsVisible = selected;
				if (selected)
				{
					outline.Bind(Border.BorderBrushProperty, outline.GetResourceObservable(LyriousTheme.SelectionBrush));
				}
				else
				{
					outline.BorderBrush = Brushes.Transparent;
				}
			}
		}
	}
}
