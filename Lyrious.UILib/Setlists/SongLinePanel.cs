using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's line in a list: its name (the first child) at the left, filling the room, and the rest
	/// of the children (e.g. its tags, media icons, key and tempo) after it at the right, in order. When
	/// the name wouldn't fit beside them on one line, they go on two lines (right-aligned, split where
	/// they take the least width), so the name gets that room; a name too long even then is trimmed
	/// (by its own <see cref="TextBlock.TextTrimming"/>).
	/// </summary>
	public sealed class SongLinePanel : Panel
	{
		/// <summary>
		/// Added to the height of the line on one line, as its least height: a list with less room
		/// above and below its rows keeps its one-line rows as high as before this way, while rows on
		/// two lines take just the room they need. 0 for none.
		/// </summary>
		public double OneLineExtraHeight { get; init; }

		// How the items after the name were laid out at the last measure: on one line, or split before
		// the item at this index (0 for one line).
		private int split;

		protected override Size MeasureOverride(Size availableSize)
		{
			if (Children.Count == 0)
			{
				return default;
			}

			var name = Children[0];
			var items = Children.Skip(1).Where(child => child.IsVisible).ToList();
			foreach (var item in items)
			{
				item.Measure(Size.Infinity);
			}

			name.Measure(Size.Infinity);
			var nameWidth = name.DesiredSize.Width;
			var oneLine = items.Sum(item => item.DesiredSize.Width);
			split = 0;
			var blockWidth = oneLine;
			var blockHeight = items.Count == 0 ? 0 : items.Max(item => item.DesiredSize.Height);

			// The name doesn't fit beside them: on two lines, where they're narrowest, if that's narrower.
			if (!double.IsInfinity(availableSize.Width) && nameWidth + oneLine > availableSize.Width && items.Count > 1)
			{
				for (var k = 1; k < items.Count; k++)
				{
					var first = items.Take(k).ToList();
					var second = items.Skip(k).ToList();
					var splitWidth = Math.Max(first.Sum(item => item.DesiredSize.Width), second.Sum(item => item.DesiredSize.Width));
					if (splitWidth < blockWidth)
					{
						blockWidth = splitWidth;
						split = k;
						blockHeight = first.Max(item => item.DesiredSize.Height) + second.Max(item => item.DesiredSize.Height);
					}
				}
			}

			var room = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - blockWidth);
			name.Measure(new Size(room, double.PositiveInfinity));
			var width = double.IsInfinity(availableSize.Width) ? name.DesiredSize.Width + blockWidth : availableSize.Width;
			// At least as high as on one line with the extra height (see OneLineExtraHeight).
			var oneLineHeight = Math.Max(name.DesiredSize.Height, items.Count == 0 ? 0 : items.Max(item => item.DesiredSize.Height));
			return new Size(width, Math.Max(Math.Max(name.DesiredSize.Height, blockHeight), oneLineHeight + OneLineExtraHeight));
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (Children.Count == 0)
			{
				return finalSize;
			}

			var name = Children[0];
			var items = Children.Skip(1).Where(child => child.IsVisible).ToList();
			var lines = split > 0 && split < items.Count
				? new[] { items.Take(split).ToList(), items.Skip(split).ToList() }
				: new[] { items };
			var blockWidth = lines.Max(line => line.Sum(item => item.DesiredSize.Width));
			var lineHeights = lines.Select(line => line.Count == 0 ? 0 : line.Max(item => item.DesiredSize.Height)).ToArray();
			var blockHeight = lineHeights.Sum();

			// The name: the room left of the items, in the middle of the height.
			var nameWidth = Math.Max(0, finalSize.Width - blockWidth);
			var nameHeight = Math.Min(name.DesiredSize.Height, finalSize.Height);
			name.Arrange(new Rect(0, (finalSize.Height - nameHeight) / 2, nameWidth, nameHeight));

			// The items: each line right-aligned, the lines together in the middle of the height.
			var top = (finalSize.Height - blockHeight) / 2;
			for (var i = 0; i < lines.Length; i++)
			{
				var x = finalSize.Width - lines[i].Sum(item => item.DesiredSize.Width);
				foreach (var item in lines[i])
				{
					var size = item.DesiredSize;
					item.Arrange(new Rect(x, top + (lineHeights[i] - size.Height) / 2, size.Width, size.Height));
					x += size.Width;
				}

				top += lineHeights[i];
			}

			return finalSize;
		}
	}
}
