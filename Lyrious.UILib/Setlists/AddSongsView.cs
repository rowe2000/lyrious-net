using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The group's songbook, to pick songs to add to a setlist: tap songs to select them, then Add.
	/// Songs already in the setlist are marked, and can be added again.
	/// </summary>
	public sealed class AddSongsView : UserControl
	{
		/// <summary>Raised with the selected songs, in songbook order, when the user adds them.</summary>
		public event Action<IReadOnlyList<SongDetails>>? SongsAdded;

		/// <summary>Raised when the user goes back to the setlist without adding.</summary>
		public event Action? BackRequested;

		/// <param name="songsInSetlist">Ids of the songs the setlist already has.</param>
		public AddSongsView(SetlistSummary setlist, IReadOnlyList<SongDetails> songbook, IReadOnlySet<Guid> songsInSetlist)
		{
			var selected = new List<SongDetails>();
			var add = new Button { Content = "Add songs", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 12), Margin = new Thickness(0, 16, 0, 0) };
			add.Classes.Add("accent");
			add.Click += (_, _) => SongsAdded?.Invoke(songbook.Where(selected.Contains).ToArray());
			DockPanel.SetDock(add, Dock.Bottom);

			void Toggle(SongDetails song, bool on)
			{
				selected.Remove(song);
				if (on)
				{
					selected.Add(song);
				}

				add.IsEnabled = selected.Count > 0;
				add.Content = selected.Count switch { 0 => "Add songs", 1 => "Add 1 song", _ => $"Add {selected.Count} songs" };
			}

			var rows = songbook.Select(song => (Control)SongRow(song, songsInSetlist.Contains(song.Id), Toggle)).ToArray();

			var content = new DockPanel();
			if (rows.Length > 0)
			{
				content.Children.Add(add);
			}

			content.Children.Add(PageLayout.List(rows, "There are no songs in the songbook yet."));

			Content = PageLayout.Page(
				"Add songs",
				content,
				backText: $"‹ {setlist.Name}",
				back: () => BackRequested?.Invoke(),
				subtitle: "Select songs from the songbook");
		}

		/// <summary>One songbook song: name, then "in setlist" and key; stays highlighted while selected.</summary>
		private static ToggleButton SongRow(SongDetails song, bool inSetlist, Action<SongDetails, bool> toggle)
		{
			var detail = string.Join(" · ", new[] { inSetlist ? "in setlist" : null, string.IsNullOrWhiteSpace(song.Key) ? null : song.Key }.Where(part => part != null));

			var row = new DockPanel();
			if (detail.Length > 0)
			{
				var detailText = new TextBlock { Text = detail, Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
				DockPanel.SetDock(detailText, Dock.Right);
				row.Children.Add(detailText);
			}

			row.Children.Add(new TextBlock { Text = song.Name, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

			var button = new ToggleButton
			{
				Content = row,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 12),
			};
			button.IsCheckedChanged += (_, _) => toggle(song, button.IsChecked == true);
			return button;
		}
	}
}
