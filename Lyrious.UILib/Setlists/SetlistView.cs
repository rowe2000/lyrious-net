using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The songs of one setlist, in order; pick one to show its lyrics.</summary>
	public sealed class SetlistView : UserControl
	{
		/// <summary>Raised when the user picks a song.</summary>
		public event Action<SetlistEntry>? SongSelected;

		/// <summary>Raised when the user wants the list of all setlists.</summary>
		public event Action? AllSetlistsRequested;

		public SetlistView(SetlistSummary setlist, IReadOnlyList<SetlistEntry> entries)
		{
			var rows = entries
				.Select((entry, index) => (Control)PageLayout.ListRow(
					$"{index + 1}. {entry.Song.Name}",
					string.IsNullOrWhiteSpace(entry.Song.Key) ? null : entry.Song.Key,
					() => SongSelected?.Invoke(entry)))
				.ToArray();

			Content = PageLayout.Page(
				setlist.Name,
				PageLayout.List(rows, "There are no songs in this setlist yet."),
				backText: "‹ All setlists",
				back: () => AllSetlistsRequested?.Invoke(),
				subtitle: entries.Count == 1 ? "1 song" : $"{entries.Count} songs");
		}
	}
}
