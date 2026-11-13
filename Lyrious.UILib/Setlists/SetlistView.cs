using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The songs of one setlist, in order; pick one to show its lyrics, or add songs from the songbook.</summary>
	public sealed class SetlistView : UserControl
	{
		private const string AddIcon = "M12,5 V19 M5,12 H19";

		/// <summary>Raised when the user picks a song.</summary>
		public event Action<SetlistEntry>? SongSelected;

		/// <summary>Raised when the user wants the list of all setlists.</summary>
		public event Action? AllSetlistsRequested;

		/// <summary>Raised when the user wants to add songs from the songbook.</summary>
		public event Action? AddSongsRequested;

		public SetlistView(SetlistSummary setlist, IReadOnlyList<SetlistEntry> entries)
		{
			var addButton = HomeView.IconButton(AddIcon, "Add songs from the songbook");
			addButton.Click += (_, _) => AddSongsRequested?.Invoke();

			var rows = entries
				.Select((entry, index) => (Control)PageLayout.ListRow(
					$"{index + 1}. {entry.Song.Name}",
					PageLayout.SongInfo(entry.Song),
					() => SongSelected?.Invoke(entry)))
				.ToArray();

			Content = PageLayout.Page(
				setlist.Name,
				PageLayout.List(rows, "There are no songs in this setlist yet."),
				backText: "‹ All setlists",
				back: () => AllSetlistsRequested?.Invoke(),
				subtitle: entries.Count == 1 ? "1 song" : $"{entries.Count} songs",
				titleAction: addButton);
		}
	}
}
