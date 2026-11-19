using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Shown in the lyrics' place when no song is chosen yet (e.g. after login, when the band has no
	/// current song): says so, and offers the ways to choose one: from the current setlist (in the
	/// right drawer), from another setlist, or from the songbook.
	/// </summary>
	public sealed class NoSongView : UserControl
	{
		/// <summary>Raised when the user wants to choose a song from the songbook.</summary>
		public event Action? SongbookRequested;

		/// <summary>Raised when the user wants to choose a setlist.</summary>
		public event Action? SetlistsRequested;

		/// <summary>Raised when the user wants to see the current setlist's songs (in the right drawer).</summary>
		public event Action? CurrentSetlistRequested;

		/// <param name="band">The band's name.</param>
		/// <param name="setlistName">The current setlist's name; null if there's none.</param>
		public NoSongView(string band, string? setlistName)
		{
			// A note: a music note in a circle.
			var icon = new Avalonia.Controls.Shapes.Path
			{
				Data = Geometry.Parse("M9,18 A2.5,2.5 0 1 1 9,17.9 M11.5,18 V6 L18,4.5 V15.5 M18,15.5 A2.5,2.5 0 1 1 18,15.4"),
				StrokeThickness = 1.6,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 64,
				Height = 64,
				Stretch = Stretch.Uniform,
				Opacity = 0.5,
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, icon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));

			var panel = new StackPanel
			{
				Spacing = 16,
				MaxWidth = 440,
				Margin = new Thickness(16),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Children =
				{
					icon,
					new TextBlock { Text = "No song chosen yet", FontSize = 26, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
					new TextBlock
					{
						Text = setlistName == null
							? $"You haven't chosen a song yet. Pick one from {band}'s songbook, or choose a setlist to play from."
							: $"You haven't chosen a song yet. Pick one from the setlist {setlistName}, from another setlist, or from {band}'s songbook.",
						TextWrapping = TextWrapping.Wrap,
						TextAlignment = TextAlignment.Center,
						Opacity = 0.8,
					},
				},
			};

			if (setlistName != null)
			{
				panel.Children.Add(Choice($"Songs in {setlistName}", () => CurrentSetlistRequested?.Invoke(), accent: true));
			}

			panel.Children.Add(Choice("Choose a song from the songbook", () => SongbookRequested?.Invoke(), accent: setlistName == null));
			panel.Children.Add(Choice(setlistName == null ? "Choose a setlist" : "Choose another setlist", () => SetlistsRequested?.Invoke(), accent: false));
			Content = panel;
		}

		private static Button Choice(string text, Action choose, bool accent)
		{
			var button = new Button
			{
				Content = text,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				Padding = new Thickness(16, 10),
			};
			if (accent)
			{
				button.Classes.Add("accent");
			}

			button.Click += (_, _) => choose();
			return button;
		}
	}
}
