using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Picks a colour by hue, saturation and lightness: a square of the hue's colours (saturation
	/// across, from grey to the pure hue; lightness down, from white through the hue to black) and a
	/// bar of hues below it. Drag in either; <see cref="ColorChanged"/> follows as you go.
	/// </summary>
	public sealed class HslColorPicker : UserControl
	{
		private const double AreaWidth = 260;
		private const double AreaHeight = 170;
		private const double MarkerSize = 14;

		private readonly Border hueLayer = new() { Width = AreaWidth, Height = AreaHeight };
		private readonly Canvas area = new() { Width = AreaWidth, Height = AreaHeight, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Cross) };
		private readonly Ellipse areaMarker = new() { Width = MarkerSize, Height = MarkerSize, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
		private readonly Canvas hueBar = new() { Width = AreaWidth, Height = 18, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
		private readonly Border hueMarker = new() { Width = 6, Height = 22, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2), IsHitTestVisible = false };

		private double hue;        // 0–360
		private double saturation; // 0–1
		private double lightness;  // 0–1

		/// <summary>Raised with the new colour while it's being picked.</summary>
		public event Action<Color>? ColorChanged;

		/// <summary>The colour picked.</summary>
		public Color Color => FromHsl(hue, saturation, lightness);

		public HslColorPicker()
		{
			// The square: the hue from grey (left) to pure (right), then white (top) through clear
			// (middle) to black (bottom) over it. For a hue, that's exactly lightness down and saturation across.
			var shade = new Border
			{
				Width = AreaWidth,
				Height = AreaHeight,
				IsHitTestVisible = false,
				Background = new LinearGradientBrush
				{
					StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
					EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
					GradientStops =
					{
						new GradientStop(Colors.White, 0),
						new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5),
						new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5),
						new GradientStop(Colors.Black, 1),
					},
				},
			};
			hueLayer.IsHitTestVisible = false;
			area.Children.Add(hueLayer);
			area.Children.Add(shade);
			area.Children.Add(areaMarker);
			var areaFrame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = area };

			// The hues, red to red.
			var rainbow = new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
			};
			for (var i = 0; i <= 6; i++)
			{
				rainbow.GradientStops.Add(new GradientStop(FromHsl(i * 60 % 360, 1, 0.5), i / 6.0));
			}

			hueBar.Children.Add(new Border { Width = AreaWidth, Height = 18, CornerRadius = new CornerRadius(9), Background = rainbow, IsHitTestVisible = false });
			hueBar.Children.Add(hueMarker);

			WireDrag(area, point =>
			{
				saturation = Math.Clamp(point.X / AreaWidth, 0, 1);
				lightness = Math.Clamp(1 - point.Y / AreaHeight, 0, 1);
			});
			WireDrag(hueBar, point => hue = Math.Clamp(point.X / AreaWidth, 0, 1) * 360 % 360);

			Content = new StackPanel { Spacing = 12, Children = { areaFrame, hueBar } };
			SetColor(Colors.SteelBlue);
		}

		/// <summary>Shows the colour (without raising <see cref="ColorChanged"/>).</summary>
		public void SetColor(Color color)
		{
			(var h, saturation, lightness) = ToHsl(color);
			// A grey has no hue of its own: the one shown stays.
			if (saturation > 0.001 && lightness is > 0.001 and < 0.999)
			{
				hue = h;
			}

			Refresh();
		}

		/// <summary>Picks from where the pointer is pressed, and follows it while it's held down.</summary>
		private void WireDrag(Control surface, Action<Point> pick)
		{
			var dragging = false;
			void PickAt(PointerEventArgs e)
			{
				pick(e.GetPosition(surface));
				Refresh();
				ColorChanged?.Invoke(Color);
			}

			surface.PointerPressed += (_, e) =>
			{
				dragging = true;
				e.Pointer.Capture(surface);
				PickAt(e);
				e.Handled = true;
			};
			surface.PointerMoved += (_, e) =>
			{
				if (dragging)
				{
					PickAt(e);
				}
			};
			surface.PointerReleased += (_, e) =>
			{
				dragging = false;
				e.Pointer.Capture(null);
			};
		}

		/// <summary>The square in the current hue, and both markers where the colour is.</summary>
		private void Refresh()
		{
			hueLayer.Background = new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
				GradientStops =
				{
					new GradientStop(FromHsl(hue, 0, 0.5), 0),
					new GradientStop(FromHsl(hue, 1, 0.5), 1),
				},
			};

			Canvas.SetLeft(areaMarker, saturation * AreaWidth - MarkerSize / 2);
			Canvas.SetTop(areaMarker, (1 - lightness) * AreaHeight - MarkerSize / 2);
			// Dark ring on light colours, light ring on dark ones.
			areaMarker.Stroke = lightness > 0.6 ? Brushes.Black : Brushes.White;
			Canvas.SetLeft(hueMarker, hue / 360 * AreaWidth - 3);
			Canvas.SetTop(hueMarker, -2);
		}

		/// <summary>The colour of a hue (0–360), saturation and lightness (0–1).</summary>
		public static Color FromHsl(double h, double s, double l)
		{
			var c = (1 - Math.Abs(2 * l - 1)) * s;
			var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
			var m = l - c / 2;
			var (r, g, b) = (h % 360) switch
			{
				< 60 => (c, x, 0d),
				< 120 => (x, c, 0d),
				< 180 => (0d, c, x),
				< 240 => (0d, x, c),
				< 300 => (x, 0d, c),
				_ => (c, 0d, x),
			};

			static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
			return Color.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));
		}

		/// <summary>A colour's hue (0–360), saturation and lightness (0–1).</summary>
		public static (double H, double S, double L) ToHsl(Color color)
		{
			double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
			var max = Math.Max(r, Math.Max(g, b));
			var min = Math.Min(r, Math.Min(g, b));
			var l = (max + min) / 2;
			var d = max - min;
			if (d < 1e-9)
			{
				return (0, 0, l);
			}

			var s = d / (1 - Math.Abs(2 * l - 1));
			double h;
			if (max == r)
			{
				h = 60 * ((g - b) / d % 6);
			}
			else if (max == g)
			{
				h = 60 * ((b - r) / d + 2);
			}
			else
			{
				h = 60 * ((r - g) / d + 4);
			}

			return ((h + 360) % 360, Math.Clamp(s, 0, 1), l);
		}
	}
}
