using System;
using System.Collections.Generic;
using System.Linq;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's tags, kept as one text field separated by spaces (e.g. "ballad christmas"); commas and
	/// semicolons separate them too, and become spaces when saved. A tag is one word. The same tag
	/// twice (ignoring case) is kept once, as it was first written.
	/// </summary>
	public static class SongTags
	{
		/// <summary>What separates tags: spaces (and tabs and line breaks), commas and semicolons.</summary>
		public static readonly char[] Separators = [' ', '\t', '\r', '\n', ',', ';'];

		/// <summary>The tags in the text (separated by spaces, commas or semicolons), in order; a "#" in front of a tag is left out.</summary>
		public static IReadOnlyList<string> Parse(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return [];
			}

			return text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(tag => tag.TrimStart('#').Trim())
				.Where(tag => tag.Length > 0)
				.Distinct(StringComparer.CurrentCultureIgnoreCase)
				.ToArray();
		}

		/// <summary>The tags as the text field holds them.</summary>
		public static string Join(IEnumerable<string> tags) => string.Join(" ", tags);

		/// <summary>
		/// The tags as they're saved: each once, trimmed, sorted alphabetically (ignoring case), e.g.
		/// "Rock, ballad #christmas" is "ballad christmas Rock".
		/// </summary>
		public static string Tidy(string? text)
			=> Join(Parse(text).Order(StringComparer.CurrentCultureIgnoreCase));

		/// <summary>The tags in the text, sorted alphabetically (ignoring case), as they're shown.</summary>
		public static IReadOnlyList<string> Sorted(string? text)
			=> Parse(text).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();

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
