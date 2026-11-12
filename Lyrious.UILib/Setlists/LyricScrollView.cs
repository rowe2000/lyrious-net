using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>A song's lyrics in large type, scrollable, with its key, tempo and beat.</summary>
	public sealed class LyricScrollView : UserControl
	{
		/// <summary>Raised when the user wants to go back to the song's setlist.</summary>
		public event Action? BackRequested;

		public LyricScrollView(SetlistSummary setlist, SetlistEntry entry)
		{
			var song = entry.Song;
			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);

			var lyrics = new TextBlock
			{
				Text = hasLyrics ? song.Lyrics : "There are no lyrics for this song yet.",
				FontSize = hasLyrics ? 24 : 16,
				LineHeight = hasLyrics ? 36 : double.NaN,
				Opacity = hasLyrics ? 1 : 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 0, 0, 48),
			};

			Content = PageLayout.Page(
				song.Name,
				new ScrollViewer { Content = lyrics },
				backText: $"‹ {setlist.Name}",
				back: () => BackRequested?.Invoke(),
				subtitle: Details(song));
		}

		private static string Details(SongDetails song)
		{
			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(song.Key))
			{
				parts.Add($"Key {song.Key}");
			}

			if (song.Tempo > 0)
			{
				parts.Add($"{song.Tempo.ToString("0.#", CultureInfo.InvariantCulture)} bpm");
			}

			if (!string.IsNullOrWhiteSpace(song.Beat))
			{
				parts.Add(song.Beat);
			}

			return string.Join(" · ", parts);
		}
	}
}
