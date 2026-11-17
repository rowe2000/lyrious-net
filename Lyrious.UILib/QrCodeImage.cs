using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using QRCoder;
using Path = Avalonia.Controls.Shapes.Path;

namespace Lyrious.UILib
{
	/// <summary>A QR code, drawn black on white whatever the theme (cameras read it best that way).</summary>
	public static class QrCodeImage
	{
		/// <param name="text">What the code says, e.g. a web address.</param>
		/// <param name="size">The width and height of the square, its narrow white margin included.</param>
		public static Control Create(string text, double size)
		{
			using var generator = new QRCodeGenerator();
			using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
			// The squares, with the white margin a code needs around it (4 squares wide).
			var modules = data.ModuleMatrix;
			var count = modules.Count;

			// Each row's runs of black squares, as rectangles.
			var geometry = new StreamGeometry();
			using (var context = geometry.Open())
			{
				for (var y = 0; y < count; y++)
				{
					var row = modules[y];
					for (var x = 0; x < count; x++)
					{
						if (!row[x])
						{
							continue;
						}

						var start = x;
						while (x + 1 < count && row[x + 1])
						{
							x++;
						}

						context.BeginFigure(new Point(start, y), true);
						context.LineTo(new Point(x + 1, y));
						context.LineTo(new Point(x + 1, y + 1));
						context.LineTo(new Point(start, y + 1));
						context.EndFigure(true);
					}
				}
			}

			var squares = new Path { Data = geometry, Fill = Brushes.Black, Stretch = Stretch.Uniform };
			// Sharp edges: smoothing leaves hairlines between the rows.
			RenderOptions.SetEdgeMode(squares, EdgeMode.Aliased);
			return new Border
			{
				Width = size,
				Height = size,
				Background = Brushes.White,
				CornerRadius = new CornerRadius(2),
				HorizontalAlignment = HorizontalAlignment.Left,
				// The path is stretched to its black squares; the white margin is left around it here.
				// One square wide, the least that keeps the code apart from a dark background (the
				// matrix itself has 4 on each side, which made for a lot of white).
				Padding = new Thickness(size / (count - 8 + 2)),
				Child = squares,
			};
		}
	}
}
