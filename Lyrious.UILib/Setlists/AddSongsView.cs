using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

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


		/// <summary>Raised when the user wants to find a song on the internet for this setlist.</summary>
		public event Action? InternetSearchRequested;

		/// <param name="songsInSetlist">Ids of the songs the setlist already has.</param>
		public AddSongsView(SetlistSummary setlist, IReadOnlyList<SongDetails> songbook, IReadOnlySet<Guid> songsInSetlist)
		{
			var selected = new List<SongDetails>();
			var add = new Button { Content = L.T("Add songs"), IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 12), Margin = new Thickness(0, 16, 0, 0) };
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
				add.Content = selected.Count switch { 0 => L.T("Add songs"), 1 => L.T("Add 1 song"), _ => L.T("Add {0} songs", selected.Count) };
			}

			var rows = songbook.Select(song => (Control)SongRow(song, songsInSetlist.Contains(song.Id), Toggle)).ToArray();

			// At the top: find a song that isn't in the songbook yet.
			var internet = new Button { Content = L.T("Find online…"), Margin = new Thickness(0, 0, 0, 16) };
			internet.Click += (_, _) => InternetSearchRequested?.Invoke();
			DockPanel.SetDock(internet, Dock.Top);

			var content = new DockPanel { Children = { internet } };
			if (rows.Length > 0)
			{
				content.Children.Add(add);
			}

			content.Children.Add(PageLayout.List(rows, L.T("There are no songs in the songbook yet.")));

			Content = PageLayout.Page(
				L.T("Add songs"),
				content,
				subtitle: L.T("To {0} · select songs from the songbook", setlist.Name),
				closable: true);
		}

		/// <summary>
		/// One songbook song, as the songbook shows it (name and tags, then "in setlist", key, tempo
		/// and beat); stays highlighted while selected.
		/// </summary>
		private static ToggleButton SongRow(SongDetails song, bool inSetlist, Action<SongDetails, bool> toggle)
		{
			var button = new ToggleButton
			{
				Content = SongsView.SongRow(song, inSetlist ? L.T("in setlist") : null),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 12),
			};
			button.IsCheckedChanged += (_, _) => toggle(song, button.IsChecked == true);
			return button;
		}
	}
}
