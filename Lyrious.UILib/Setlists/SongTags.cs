using System;
using System.Collections.Generic;
using System.Linq;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's tags, kept as one text field separated by commas (e.g. "christmas, ballad"). Tags are
	/// trimmed, and the same tag twice (ignoring case) is kept once, as it was first written.
	/// </summary>
	public static class SongTags
	{
		/// <summary>The tags in the text, in order (a "#" in front of a tag is left out).</summary>
		public static IReadOnlyList<string> Parse(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return [];
			}

			return text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(tag => tag.TrimStart('#').Trim())
				.Where(tag => tag.Length > 0)
				.Distinct(StringComparer.CurrentCultureIgnoreCase)
				.ToArray();
		}

		/// <summary>The tags as the text field holds them.</summary>
		public static string Join(IEnumerable<string> tags) => string.Join(", ", tags);

		/// <summary>Whether the song has the tag (ignoring case).</summary>
		public static bool Has(SongDetails song, string tag)
			=> Parse(song.Tags).Contains(tag, StringComparer.CurrentCultureIgnoreCase);

		/// <summary>Every tag used by the songs, sorted, each once.</summary>
		public static IReadOnlyList<string> AllOf(IEnumerable<SongDetails> songs)
			=> songs.SelectMany(song => Parse(song.Tags))
				.Distinct(StringComparer.CurrentCultureIgnoreCase)
				.Order(StringComparer.CurrentCultureIgnoreCase)
				.ToArray();
	}
}
