using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The header above every song's lyrics: ChordPro designed by the band (Group.LyricsHeader), with
	/// the song's details for placeholders. It's put in front of the lyrics before they're shown, so it
	/// scrolls, zooms and transposes with them, like the lyrics' own first lines.
	/// <para>
	/// Placeholders (any case): $songname, $artist, $key, $tempo, $beat and $tags (the song's tags,
	/// separated by spaces: "ballad christmas Rock"). $key is the key the
	/// lyrics are written in, so in brackets ("[$key]") it's a chord that's transposed with the
	/// lyrics' chords and shows the key that's played. A line whose placeholders were all empty (e.g.
	/// "## $artist" for a song without an artist) is left out.
	/// </para>
	/// </summary>
	public static partial class LyricsHeader
	{
		/// <summary>
		/// The header when the band hasn't designed its own: the key as a chord over the song's name, as a
		/// heading, and the song's tags under it as a small heading (left out for a song without tags).
		/// </summary>
		public const string Default = "# [$key]$songname\n#### $tags";

		/// <summary>The longest header a band can store (as on the server).</summary>
		public const int MaxLength = 1000;

		private static string? current;

		/// <summary>
		/// The current band's header as stored: null for <see cref="Default"/>, empty for none. Set by
		/// the home view when the band, or its header, changes.
		/// </summary>
		public static string? Current
		{
			get => current;
			set
			{
				if (current == value)
				{
					return;
				}

				current = value;
				Changed?.Invoke();
			}
		}

		/// <summary>Raised (on the UI thread) when <see cref="Current"/> changes, so the shown lyrics are rendered again.</summary>
		public static event Action? Changed;

		/// <summary>The header that's used for a stored value: null is <see cref="Default"/>.</summary>
		public static string Definition(string? stored) => stored ?? Default;

		/// <summary>
		/// The header for <paramref name="song"/> as ChordPro: <paramref name="definition"/> with its
		/// placeholders filled in; empty when there's no header.
		/// </summary>
		/// <param name="key">The key the song's lyrics are written in (the placeholder $key).</param>
		public static string Expand(string definition, SongDetails song, string key)
		{
			if (string.IsNullOrWhiteSpace(definition))
			{
				return "";
			}

			var values = new (string Name, string Value)[]
			{
				("songname", song.Name.Trim()),
				("artist", song.Artist.Trim()),
				("key", key.Trim()),
				("tempo", song.Tempo > 0 ? song.Tempo.ToString("0.##", CultureInfo.InvariantCulture) : ""),
				("beat", song.Beat.Trim()),
				// The song's tags in the order they're shown elsewhere, separated by spaces: "ballad christmas Rock".
				("tags", string.Join(" ", SongTags.Sorted(song.Tags))),
			};

			var lines = definition.Replace("\r\n", "\n").Split('\n').Select(line =>
			{
				var hadPlaceholder = false;
				var expanded = Placeholder().Replace(line, match =>
				{
					var value = values.FirstOrDefault(v => v.Name.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
					if (value.Name == null)
					{
						// Not one of ours: left as written.
						return match.Value;
					}

					hadPlaceholder = true;
					return value.Value;
				});

				// An empty chord ("[$key]" for a song without a key) isn't shown as "[]".
				expanded = expanded.Replace("[]", "");
				// A line that was only placeholders, all empty, is left out (not an empty heading or gap).
				var empty = hadPlaceholder && expanded.Trim().TrimStart('#').Trim().Length == 0;
				return empty ? null : expanded;
			}).OfType<string>();

			return string.Join('\n', lines).Trim('\n');
		}

		/// <summary>
		/// The lyrics with the header in front: the header, an empty line, then the lyrics. Just the
		/// lyrics when there's no header.
		/// </summary>
		public static string Prepend(string definition, SongDetails song, string key, string lyrics)
			=> Expand(definition, song, key) is { Length: > 0 } header ? header + "\n\n" + lyrics : lyrics;

		// "$name": all the letters after the '$', so "$songname" is one name.
		[GeneratedRegex(@"\$([A-Za-z]+)")]
		private static partial Regex Placeholder();
	}
}
