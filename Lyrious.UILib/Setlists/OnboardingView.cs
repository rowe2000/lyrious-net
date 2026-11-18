using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Shown after login. The first time on a device: a short introduction to the app. After that:
	/// what changed in the member's bands since their last visit (bands they were added to, new
	/// members, new and changed setlists and songs) and the app's release notes since then.
	/// Continue (or ✕) goes on to the start page.
	/// </summary>
	public sealed class OnboardingView : UserControl
	{
		/// <summary>Raised when the member goes on (Continue).</summary>
		public event Action? Continued;

		/// <param name="lastVisit">When the member last saw this screen on this device; null the first time.</param>
		/// <param name="changes">The changes since then; null when they couldn't be found out (e.g. offline).</param>
		public OnboardingView(string memberName, DateTime? lastVisit, IReadOnlyList<Change>? changes)
		{
			var column = new StackPanel { Spacing = 12 };
			string title;
			if (lastVisit is not { } since)
			{
				title = "Welcome to Lyrious";
				column.Children.Add(Paragraph($"Hi {memberName}! Lyrious keeps your band's songs, setlists and lyrics in one place, and on every member's screen at once."));
				column.Children.Add(Bullets(
				[
					"Bands: join or start bands, invite members, and pick the band you play with now from the menu.",
					"Your private band: a place of your own for songs and setlists only you can see.",
					"Setlists and songbook: put the band's songs in setlists for each gig; the songbook holds them all.",
					"Lyrics that scroll in time: time tags such as [0:12], or a '|' for each bar, keep the lyrics in step with the song; the grid button shows the chords in bar columns.",
					"Band sync (the globe): follow the song the band is on, and play and stop together.",
					"Media: link a song to Spotify, YouTube or a sound file, and play it from the lyrics page.",
				]));
				column.Children.Add(Paragraph("The menu at the top left has your bands, setlists and songbook; the list at the top right is the current setlist."));
			}
			else
			{
				title = "Since your last visit";
				column.Children.Add(new TextBlock { Text = $"Last visit {since.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)}", Opacity = 0.7 });
				if (changes == null)
				{
					column.Children.Add(Paragraph("Couldn't find out what changed (the server couldn't be reached)."));
				}
				else if (changes.Count == 0)
				{
					column.Children.Add(Paragraph("Nothing new since your last visit."));
				}
				else
				{
					// By band, the band with the latest change first.
					foreach (var band in changes.GroupBy(change => change.Band))
					{
						column.Children.Add(Heading(band.Key.Length > 0 ? band.Key : "Other"));
						column.Children.Add(Bullets(band.Select(Describe).ToList()));
					}
				}

				var notes = ReleaseNotes.Since(since);
				if (notes.Count > 0)
				{
					column.Children.Add(Heading("What's new in Lyrious"));
					foreach (var note in notes)
					{
						column.Children.Add(ReleaseNoteBlock(note));
					}
				}
			}

			var go = new Button { Content = "Continue", Classes = { "accent" }, Height = 40, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
			go.Click += (_, _) => Continued?.Invoke();
			column.Children.Add(go);

			Content = PageLayout.Page(
				title,
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				closable: true);
		}

		/// <summary>E.g. "New song: Blackbird", "Changed setlist: Friday", "New member: Anna", "You were added to the band".</summary>
		private static string Describe(Change change) => change.Kind switch
		{
			ChangeKind.Band => $"You were added to the band {change.Name}",
			ChangeKind.Member => $"New member: {change.Name}",
			ChangeKind.Setlist => $"{(change.IsNew ? "New" : "Changed")} setlist: {change.Name}",
			_ => $"{(change.IsNew ? "New" : "Changed")} song: {change.Name}",
		};

		/// <summary>A release's version and date, and its notes as bullets.</summary>
		internal static Control ReleaseNoteBlock(ReleaseNote note) => new StackPanel
		{
			Spacing = 6,
			Children =
			{
				new TextBlock { Text = $"{note.Version} · {note.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}", FontWeight = FontWeight.SemiBold },
				Bullets(note.Notes),
			},
		};

		private static TextBlock Heading(string text)
			=> new() { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };

		private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

		private static Control Bullets(IReadOnlyList<string> lines)
		{
			var list = new StackPanel { Spacing = 4 };
			foreach (var line in lines)
			{
				var dot = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0) };
				DockPanel.SetDock(dot, Dock.Left);
				list.Children.Add(new DockPanel { Children = { dot, Paragraph(line) } });
			}

			return list;
		}
	}
}
