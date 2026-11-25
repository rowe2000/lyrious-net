using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Song titles as streaming services write them ("Song (feat. Someone) - Remastered 2011") made into
	/// the song's own title, and found among the songbook's songs by it.
	/// </summary>
	public static partial class SongTitles
	{
		/// <summary>
		/// The song's own title: without what's in brackets ("(feat. …)", "[Live]") and what follows " - "
		/// ("- Remastered 2011", "- Radio Edit"); the title as it is when that would leave nothing.
		/// </summary>
		public static string Clean(string title)
		{
			var cleaned = Bracketed().Replace(title, " ");
			var dash = cleaned.IndexOf(" - ", StringComparison.Ordinal);
			if (dash > 0)
			{
				cleaned = cleaned[..dash];
			}

			cleaned = Spaces().Replace(cleaned, " ").Trim();
			return cleaned.Length > 0 ? cleaned : title.Trim();
		}

		/// <summary>
		/// The songbook's song with this title (by its own title: letters and digits, any case), the one
		/// by the same artist when there are more; null when none has it.
		/// </summary>
		public static SongDetails? Find(IReadOnlyList<SongDetails> songs, string title, string artist)
		{
			var key = Key(Clean(title));
			if (key.Length == 0)
			{
				return null;
			}

			var matches = songs.Where(song => Key(Clean(song.Name)) == key).ToList();
			if (matches.Count <= 1)
			{
				return matches.FirstOrDefault();
			}

			// The first artist named ("Artist, Other artist").
			var first = Key(artist.Split(',')[0]);
			return matches.FirstOrDefault(song => first.Length > 0 && Key(song.Artist).Contains(first, StringComparison.Ordinal)) ?? matches[0];
		}

		/// <summary>Lower case letters and digits, one space between words.</summary>
		private static string Key(string? text)
		{
			var builder = new StringBuilder();
			foreach (var c in (text ?? "").ToLowerInvariant())
			{
				builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
			}

			return Spaces().Replace(builder.ToString(), " ").Trim();
		}

		[GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
		private static partial Regex Bracketed();

		[GeneratedRegex(@"\s+")]
		private static partial Regex Spaces();
	}
}
