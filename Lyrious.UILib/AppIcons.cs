using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib
{
	/// <summary>
	/// The icons of the app's places (line drawings on a 24×24 grid), shared by the left drawer's
	/// items and the titles of the pages they open.
	/// </summary>
	public static class AppIcons
	{
		public const string Band = "M9,11 A3.2,3.2 0 1 0 9,4.6 A3.2,3.2 0 1 0 9,11 M3,20 C3,16 5.7,13.5 9,13.5 C12.3,13.5 15,16 15,20 M16,4.8 A3,3 0 0 1 16,10.8 M18,13.8 C20,14.5 21,16.8 21,20";
		public const string Setlist = "M9,6 H20 M9,12 H20 M9,18 H20 M4.5,6 H5 M4.5,12 H5 M4.5,18 H5";
		public const string Songbook = "M5,4 H18 A1,1 0 0 1 19,5 V20 H6 A1,1 0 0 1 5,19 Z M9,4 V20 M12,8 H16";
		public const string FindOnline = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M3,12 H21 M12,3 C15.5,6 15.5,18 12,21 C8.5,18 8.5,6 12,3";
		public const string Playlog = "M12,7 V12 L15,14 M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3";
		public const string Settings = "M4,6 H13 M17,6 H20 M15,4 V8 M4,12 H7 M11,12 H20 M9,10 V14 M4,18 H14 M18,18 H20 M16,16 V20";
		public const string SoundFiles = "M4,9 H8 L13,5 V19 L8,15 H4 Z M16.5,8.5 A5,5 0 0 1 16.5,15.5";
		public const string Sync = "M20,12 A8,8 0 0 1 6,17.3 M4,12 A8,8 0 0 1 18,6.7 M18,3 V7 H14 M6,21 V17 H10";
		public const string About = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,11 V16 M12,8 H12.01";
		public const string Profile = "M12,12 A4,4 0 1 0 12,4 A4,4 0 1 0 12,12 M4,21 C4,17 7.6,14.5 12,14.5 C16.4,14.5 20,17 20,21";
		/// <summary>A MIDI (DIN) socket: a circle with a notch at the top and five pins in an arc.</summary>
		public const string Midi = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M10.5,3.2 V5.5 H13.5 V3.2 M7,12 H7.01 M8.5,15.5 H8.51 M12,17 H12.01 M15.5,15.5 H15.51 M17,12 H17.01";
		public const string Exit = "M10,4 H5 V20 H10 M14,8 L18,12 L14,16 M8,12 H18";
		// Logging out: an arrow out of a door (Exit's, the other way round).
		public const string LogOut = "M14,4 H19 V20 H14 M10,8 L6,12 L10,16 M6,12 H16";
		// A shield with a tick: managing the whole site.
		public const string SiteManager = "M12,3 L19,6 V11 C19,15.5 16,19 12,21 C8,19 5,15.5 5,11 V6 Z M9,12 L11,14 L15,10";
		public const string Trash = "M5,7 H19 M10,7 V5 H14 V7 M7,7 L8,20 H16 L17,7 M10,11 V16 M14,11 V16";

		/// <summary>
		/// The icon as a line drawing at the size given, in the colour of the text around it. The whole
		/// 24×24 grid is scaled (not just the drawing stretched to fit), so every icon keeps its size
		/// on the grid: a short list is as big as it was drawn, not blown up to fill the box.
		/// </summary>
		/// <param name="thickness">The line width on the 24×24 grid (scaled with it).</param>
		public static Avalonia.Controls.Control Create(string data, double size, double thickness = 2)
		{
			var path = new Path
			{
				Data = Geometry.Parse(data),
				StrokeThickness = thickness,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
			};
			path.Bind(Shape.StrokeProperty, path.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			return new Avalonia.Controls.Viewbox
			{
				Width = size,
				Height = size,
				Stretch = Stretch.Uniform,
				VerticalAlignment = VerticalAlignment.Center,
				Child = new Avalonia.Controls.Canvas { Width = 24, Height = 24, Children = { path } },
			};
		}
	}
}
