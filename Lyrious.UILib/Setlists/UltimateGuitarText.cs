using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song from Ultimate Guitar made into Lyrious' lyrics (ChordPro): chords over the words go into
	/// the words at their places, "[Verse 1]" becomes a heading, a line of chords alone a row of chords,
	/// guitar tab a tab block. Ultimate Guitar's own marks ([ch]…[/ch] around chords, [tab]…[/tab] around
	/// a chord line and its words) are understood and left out.
	/// </summary>
	public static partial class UltimateGuitarText
	{
		/// <summary>The song's text from Ultimate Guitar as Lyrious' lyrics (see <see cref="UltimateGuitarText"/>), with the capo first when there's one.</summary>
		public static string ToChordPro(string text, int capo = 0)
		{
			var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
				.Select(line => TabTag().Replace(line.Replace("\t", "    "), "").TrimEnd())
				.ToList();
			var output = new List<string>();
			if (capo > 0)
			{
				output.Add($"{{capo: {capo}}}");
			}

			var inTab = false;
			for (var i = 0; i < lines.Count; i++)
			{
				var marked = HasChordMark(lines[i]);
				var line = ChordTag().Replace(lines[i], "");
				var trimmed = line.Trim();

				// Guitar tab (e|---0---|): kept as it is, in a tab block.
				if (IsTabLine(trimmed))
				{
					if (!inTab)
					{
						output.Add("{sot}");
						inTab = true;
					}

					output.Add(line);
					continue;
				}

				if (inTab)
				{
					output.Add("{eot}");
					inTab = false;
				}

				if (trimmed.Length == 0)
				{
					output.Add("");
					continue;
				}

				// "[Verse 1]", "[Chorus]": a heading.
				if (SectionLine().Match(trimmed) is { Success: true } section && !IsChord(section.Groups["name"].Value))
				{
					output.Add("## " + section.Groups["name"].Value.Trim());
					continue;
				}

				if (marked || IsChordLine(trimmed))
				{
					// The words under it (not a heading, chords or tab): the chords go into them.
					var next = i + 1 < lines.Count ? lines[i + 1] : null;
					var nextTrimmed = next?.Trim() ?? "";
					if (next != null && nextTrimmed.Length > 0 && !HasChordMark(next)
					    && !IsChordLine(nextTrimmed) && !IsTabLine(nextTrimmed) && !SectionLine().IsMatch(nextTrimmed))
					{
						output.Add(Merge(line, next));
						i++;
					}
					else
					{
						// Chords alone (an intro, an instrumental part): one after the other, with repeats ("x2").
						output.Add(string.Join(" ", Token().Matches(line)
							.Select(token => token.Value)
							.Where(token => IsChord(token) || Repeat().IsMatch(token))
							.Select(token => IsChord(token) ? $"[{token}]" : token)));
					}

					continue;
				}

				output.Add(line);
			}

			if (inTab)
			{
				output.Add("{eot}");
			}

			// No blank lines at the start or the end, and never more than one in a row.
			var lyrics = new StringBuilder();
			var blank = false;
			foreach (var line in output)
			{
				if (string.IsNullOrWhiteSpace(line))
				{
					blank = lyrics.Length > 0;
					continue;
				}

				if (blank)
				{
					lyrics.Append('\n');
				}

				lyrics.Append(line).Append('\n');
				blank = false;
			}

			return lyrics.ToString().TrimEnd('\n');
		}

		/// <summary>The chords of a chord line put into the words under it, at their places: "G   C" over "Hello there" is "[G]Hel[C]lo there".</summary>
		private static string Merge(string chordLine, string words)
		{
			var result = words;
			// From the right, so the places further left stay where they were.
			foreach (var (name, at) in Chords(chordLine).Reverse())
			{
				if (at > result.Length)
				{
					result = result.PadRight(at);
				}

				result = result.Insert(at, $"[{name}]");
			}

			return result.TrimEnd();
		}

		/// <summary>The chords of a chord line, and where each starts; bar marks, repeats and the like left out.</summary>
		private static IEnumerable<(string Name, int At)> Chords(string line)
		{
			foreach (Match token in Token().Matches(line))
			{
				if (IsChord(token.Value))
				{
					yield return (token.Value, token.Index);
				}
			}
		}

		/// <summary>Whether the line has Ultimate Guitar's chord marks ([ch]…[/ch]): a chord line.</summary>
		private static bool HasChordMark(string line) => line.Contains("[ch]", System.StringComparison.OrdinalIgnoreCase);

		/// <summary>Whether the line is only chords (with bar marks, repeats such as "x2", and brackets around them).</summary>
		private static bool IsChordLine(string line)
		{
			var tokens = Token().Matches(line).Select(match => match.Value).ToList();
			return tokens.Any(IsChord) && tokens.All(token => IsChord(token) || NotChordButAllowed().IsMatch(token));
		}

		/// <summary>Whether the text is a chord: G, F#m7, Bbmaj7, Dsus4, C/E, Am(add9), N.C.</summary>
		private static bool IsChord(string text) => Chord().IsMatch(text.Trim());

		/// <summary>Whether the line is a line of guitar tab: a string's name and a bar of dashes and frets (e|--3--|).</summary>
		private static bool IsTabLine(string line) => TabLine().IsMatch(line);

		[GeneratedRegex(@"\[/?ch\]", RegexOptions.IgnoreCase)]
		private static partial Regex ChordTag();

		[GeneratedRegex(@"\[/?tab\]", RegexOptions.IgnoreCase)]
		private static partial Regex TabTag();

		[GeneratedRegex(@"\S+")]
		private static partial Regex Token();

		[GeneratedRegex(@"^(N\.?C\.?|[A-G](#|b)?(m|min|maj|M|dim|aug|sus|add|\+|-|°|ø)*\d*(\(?(maj|add|sus|b|#)?\d+\)?)*(/[A-G](#|b)?)?)$")]
		private static partial Regex Chord();

		// Bar marks, repeats ("x2", "(x4)"), dashes, and brackets around chords.
		[GeneratedRegex(@"^(\||\|\||-+|[xX]\d+|\(?[xX]?\d+[xX]?\)?|\(|\)|\.\.\.|/)$")]
		private static partial Regex NotChordButAllowed();

		[GeneratedRegex(@"^\(?[xX]\d+\)?$|^\(?\d+[xX]\)?$")]
		private static partial Regex Repeat();

		[GeneratedRegex(@"^\[(?<name>[^\[\]]+)\]$")]
		private static partial Regex SectionLine();

		[GeneratedRegex(@"^[eBGDAEbgda]\s*\|[-0-9hpbrx/\\~|()\s.*]*$")]
		private static partial Regex TabLine();
	}
}
