using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Lays its children out left to right, wrapping like words, around a control in the top right
	/// corner (<see cref="Corner"/>, e.g. a page's tools): lines beside the corner get the width left of
	/// it, lines below it the whole width, under the corner. A child that doesn't fit the line it's on
	/// goes to the next; one too wide to fit beside the corner goes below it. A line after the first
	/// that reaches only a little beside the corner (it starts before the corner's bottom) and needs
	/// more width moves down below the corner instead of wrapping.
	/// <para>
	/// It's given the width left of the corner (e.g. by a <see cref="DockPanel"/> with the corner docked
	/// right) and lays the lines below the corner out under it too, beyond its own right edge, so the
	/// corner must be at its right with nothing else there.
	/// </para>
	/// Each child sits in its line by its <see cref="Layoutable.VerticalAlignment"/> (bottom, centre or top).
	/// </summary>
	public sealed class CornerWrapPanel : Panel
	{
		private Control? corner;

		/// <summary>The control in the top right corner that the first lines go around; null for none.</summary>
		public Control? Corner
		{
			get => corner;
			set
			{
				if (corner == value)
				{
					return;
				}

				if (corner != null)
				{
					corner.LayoutUpdated -= OnCornerLayoutUpdated;
				}

				corner = value;
				if (corner != null)
				{
					// The corner's height can change without this panel's width changing: laid out again then.
					corner.LayoutUpdated += OnCornerLayoutUpdated;
				}

				InvalidateMeasure();
			}
		}

		private Size cornerSize;

		private void OnCornerLayoutUpdated(object? sender, EventArgs e)
		{
			if (CornerSize() != cornerSize)
			{
				InvalidateMeasure();
			}
		}

		// The room the corner takes: its size with its margin, nothing when it's hidden.
		private Size CornerSize()
			=> corner is { IsVisible: true } c ? new Size(c.DesiredSize.Width, c.DesiredSize.Height) : default;

		protected override Size MeasureOverride(Size availableSize)
		{
			cornerSize = CornerSize();
			var full = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width + cornerSize.Width;
			foreach (var child in Children)
			{
				child.Measure(new Size(full, double.PositiveInfinity));
			}

			var lines = Lines(availableSize.Width);
			var height = lines.Count == 0 ? 0 : lines[^1].Top + lines[^1].Height;
			var width = double.IsInfinity(availableSize.Width) ? WidestLine(lines) : availableSize.Width;
			return new Size(width, height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			foreach (var line in Lines(finalSize.Width))
			{
				foreach (var (child, left) in line.Children)
				{
					var size = child.DesiredSize;
					var top = child.VerticalAlignment switch
					{
						VerticalAlignment.Top => line.Top,
						VerticalAlignment.Center => line.Top + (line.Height - size.Height) / 2,
						_ => line.Top + line.Height - size.Height,
					};
					child.Arrange(new Rect(left, top, size.Width, size.Height));
				}
			}

			return finalSize;
		}

		private sealed record Line(double Top, double Height, List<(Control Child, double Left)> Children);

		/// <summary>The lines, from the children's measured sizes: beside the corner the width given, below it that and the corner's.</summary>
		private List<Line> Lines(double besideCorner)
		{
			var lines = new List<Line>();
			var top = 0.0;
			var height = 0.0;
			var x = 0.0;
			var current = new List<(Control, double)>();

			double WidthAt(double y) => y < cornerSize.Height ? besideCorner : besideCorner + cornerSize.Width;

			void NextLine()
			{
				if (current.Count > 0)
				{
					lines.Add(new Line(top, height, current));
					top += height;
				}

				current = [];
				height = 0;
				x = 0;
			}

			foreach (var child in Children)
			{
				if (!child.IsVisible)
				{
					continue;
				}

				var size = child.DesiredSize;
				if (x > 0 && x + size.Width > WidthAt(top))
				{
					// A line after the first that reaches only a little beside the corner: it moves down
					// below the corner, where it has the whole width, rather than wrapping once more.
					if (lines.Count > 0 && top < cornerSize.Height && x + size.Width <= besideCorner + cornerSize.Width)
					{
						top = cornerSize.Height;
					}
					else
					{
						NextLine();
					}
				}

				// Too wide to go beside the corner, even alone: below it.
				if (x == 0 && top < cornerSize.Height && size.Width > besideCorner)
				{
					top = Math.Max(top, cornerSize.Height);
				}

				current.Add((child, x));
				x += size.Width;
				height = Math.Max(height, size.Height);
			}

			NextLine();
			return lines;
		}

		private static double WidestLine(List<Line> lines)
		{
			var widest = 0.0;
			foreach (var line in lines)
			{
				if (line.Children.Count > 0)
				{
					var (last, left) = line.Children[^1];
					widest = Math.Max(widest, left + last.DesiredSize.Width);
				}
			}

			return widest;
		}
	}
}
