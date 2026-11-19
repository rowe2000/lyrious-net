using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Finds the songs sound files are recordings of, by their names: "03 - Abba - Dancing Queen.mp3"
	/// is the song "Dancing Queen" (better still by Abba). Each song goes to one file at most.
	/// </summary>
	public static partial class SoundMatching
	{
		/// <summary>
		/// For each file name, the best song for it among <paramref name="songs"/> (null if none fits),
		/// a song given to the file it fits best.
		/// </summary>
		public static IReadOnlyList<SongDetails?> Match(IReadOnlyList<string> fileNames, IReadOnlyList<SongDetails> songs)
		{
			var scores = new List<(int File, SongDetails Song, int Score)>();
			for (var i = 0; i < fileNames.Count; i++)
			{
				foreach (var song in songs)
				{
					var score = Score(fileNames[i], song);
					if (score > 0)
					{
						scores.Add((i, song, score));
					}
				}
			}

			var matched = new SongDetails?[fileNames.Count];
			var taken = new HashSet<Guid>();
			foreach (var (file, song, _) in scores.OrderByDescending(s => s.Score))
			{
				if (matched[file] == null && taken.Add(song.Id))
				{
					matched[file] = song;
				}
			}

			return matched;
		}

		/// <summary>How well a file name fits a song: 0 not at all; more is better.</summary>
		public static int Score(string fileName, SongDetails song)
		{
			var name = Normal(song.Name);
			if (name.Length == 0)
			{
				return 0;
			}

			var title = TrackNumber().Replace(Path.GetFileNameWithoutExtension(fileName), "");
			var whole = Normal(title);
			if (whole.Length == 0)
			{
				return 0;
			}

			// "Artist - Title" (or "Title - Artist"): a part that is the song's name fits best after the whole name.
			var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries).Select(Normal).ToArray();
			int score;
			if (whole == name)
			{
				score = 1000;
			}
			else if (parts.Contains(name))
			{
				score = 900;
			}
			else if ($" {whole} ".Contains($" {name} ") && name.Length >= 3)
			{
				// The name among other words, the longer the surer.
				score = 100 + name.Length;
			}
			else if ($" {name} ".Contains($" {whole} ") && whole.Length >= 4)
			{
				score = 50 + whole.Length;
			}
			else
			{
				return 0;
			}

			var artist = Normal(song.Artist);
			if (artist.Length > 0 && $" {whole} ".Contains($" {artist} "))
			{
				score += 20;
			}

			return score;
		}

		/// <summary>Lower case letters and digits, one space between words, accents kept (å, ä, ö are letters of their own).</summary>
		private static string Normal(string? text)
		{
			var builder = new StringBuilder();
			foreach (var c in (text ?? "").ToLowerInvariant())
			{
				builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
			}

			return Spaces().Replace(builder.ToString(), " ").Trim();
		}

		// "01 ", "1. ", "03 - ", "12_" in front of the title.
		[GeneratedRegex(@"^\s*\d{1,3}\s*[-._)]*\s+|^\s*\d{1,3}[-._)]+\s*")]
		private static partial Regex TrackNumber();

		[GeneratedRegex(@"\s+")]
		private static partial Regex Spaces();
	}
}
