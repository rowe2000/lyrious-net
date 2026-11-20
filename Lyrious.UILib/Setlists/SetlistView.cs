using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The songs of one setlist, in order; pick one to show its lyrics, or add songs from the songbook.
	/// The songs look as in the songbook (see <see cref="SongsView.SongRow"/>), without its check boxes;
	/// a click opens a song.
	/// </summary>
	public sealed class SetlistView : UserControl
	{
		private const string AddIcon = "M12,5 V19 M5,12 H19";

		/// <summary>Raised when the user picks a song.</summary>
		public event Action<SetlistEntry>? SongSelected;


		/// <summary>Raised when the user wants to add songs from the songbook.</summary>
		public event Action? AddSongsRequested;

		/// <param name="currentEntryId">The band's current song in this setlist (highlighted, with a check), if it's here.</param>
		public SetlistView(SetlistSummary setlist, IReadOnlyList<SetlistEntry> entries, Guid? currentEntryId = null)
		{
			var addButton = HomeView.IconButton(AddIcon, L.T("Add songs from the songbook"));
			addButton.Click += (_, _) => AddSongsRequested?.Invoke();

			var list = new SelectableList<SetlistEntry>(
				entries,
				entry => SongsView.SongRow(entry.Song, null, entry.Id == currentEntryId),
				entry => entry.Id == currentEntryId,
				_ => [],
				entry => SongSelected?.Invoke(entry),
				L.T("There are no songs in this setlist yet."),
				activateOnClick: true);

			Content = PageLayout.Page(
				setlist.Name,
				list,
				subtitle: entries.Count == 1 ? "1 song" : $"{entries.Count} songs",
				titleAction: addButton,
				closable: true);
		}
	}
}
