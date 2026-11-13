using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The band's songbook, to pick a song to show; the current song is highlighted.</summary>
	public sealed class SongsView : UserControl
	{
		/// <summary>Raised when the user picks a song.</summary>
		public event Action<SongDetails>? SongSelected;

		public SongsView(GroupSummary group, IReadOnlyList<SongDetails> songs, Guid? currentSongId)
		{
			var rows = songs
				.Select(song =>
				{
					var row = PageLayout.ListRow(song.Name, PageLayout.SongInfo(song), () => SongSelected?.Invoke(song));
					if (song.Id == currentSongId)
					{
						row.Classes.Add("accent");
					}

					return (Control)row;
				})
				.ToArray();

			Content = PageLayout.Page(
				"Songbook",
				PageLayout.List(rows, "There are no songs in the songbook yet."),
				subtitle: $"{group.Name} · choose a song");
		}
	}
}
