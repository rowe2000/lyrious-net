using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>A song from a SongbookPro backup, ready for the songbook (its id in the backup, for its sets).</summary>
	public sealed record SongbookProSong(int SourceId, string Name, string Key, float Tempo, string Beat, string Lyrics, string MediaLink, TimeSpan Duration, string Tags = "", string Artist = "");

	/// <summary>A set from a SongbookPro backup: its name and its songs' ids in the backup, in order.</summary>
	public sealed record SongbookProSet(string Name, IReadOnlyList<int> SongIds);

	/// <summary>What a SongbookPro backup holds that can be imported, and what had to be left out (and why).</summary>
	public sealed record SongbookProBackup(IReadOnlyList<SongbookProSong> Songs, IReadOnlyList<SongbookProSet> Sets, IReadOnlyList<string> Skipped);

	/// <summary>How an import went: what was added (of which <c>Replaced</c> songs overwrote ones of the same name), and what was left out.</summary>
	public sealed record SongbookProImportResult(int Songs, int Setlists, IReadOnlyList<string> Skipped, int Replaced = 0, int ReplacedSetlists = 0);

	/// <summary>
	/// Reads SongbookPro backups (a "SongbookPro Backup.zip", or the same as a .sbpbackup) and imports
	/// them into a band: its songs into the band's songbook, its sets as the band's setlists.
	/// <para>
	/// The backup is a zip with "dataFile.txt": a version line ("1.0"), then JSON with "songs" and
	/// "sets". A song has a name, author, ChordPro content, key (a number: 0 is A, counting up in
	/// semitones; 12 and up are the minor keys), capo, tempo, time signature and a link. PDF songs
	/// (type 2: their content is a PDF file) have no text, so they're left out, and so are deleted
	/// songs and sets. A song with exactly the same name as one in the songbook (case and spaces count) overwrites it,
	/// and a set with exactly the same name as one of the band's setlists replaces that setlist's songs.
	/// </para>
	/// </summary>
	public static partial class SongbookProImport
	{
		/// <summary>The most a song's lyrics can hold (as the server stores them).</summary>
		private const int MaxLyricsLength = 5000;

		private static readonly string[] KeyNames = ["A", "Bb", "B", "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab"];

		/// <summary>Reads the backup; throws <see cref="InvalidOperationException"/> when it isn't a SongbookPro backup.</summary>
		/// <param name="convert">Converts the lyrics to Lyrious' format (see <see cref="Lyrics"/>).</param>
		public static SongbookProBackup Read(Stream zip, bool convert = false)
		{
			string data;
			try
			{
				using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
				var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("dataFile.txt", StringComparison.OrdinalIgnoreCase))
				            ?? throw new InvalidOperationException(L.T("This isn't a SongbookPro backup (it has no dataFile.txt)."));
				using var reader = new StreamReader(entry.Open());
				data = reader.ReadToEnd();
			}
			catch (InvalidDataException ex)
			{
				throw new InvalidOperationException(L.T("This isn't a SongbookPro backup (it isn't a zip file)."), ex);
			}

			// A version line ("1.0") before the JSON.
			var start = data.IndexOf('{');
			JsonObject json;
			try
			{
				json = start >= 0 && JsonNode.Parse(data[start..]) is JsonObject parsed ? parsed : throw new InvalidOperationException(L.T("The backup's data couldn't be read."));
			}
			catch (JsonException ex)
			{
				throw new InvalidOperationException(L.T("The backup's data couldn't be read. {0}", ex.Message), ex);
			}

			var skipped = new List<string>();
			var songs = new List<SongbookProSong>();
			foreach (var song in (json["songs"] as JsonArray ?? []).OfType<JsonObject>())
			{
				var name = Text(song, "name").Trim();
				if (Bool(song, "Deleted"))
				{
					continue;
				}

				if (Int(song, "type") == 2)
				{
					skipped.Add($"{name} (a PDF)");
					continue;
				}

				var lyrics = Lyrics(song, convert);
				var tags = convert ? Tags(song) : "";
				// Converted, a ✓ in the name or the lyrics becomes the tag "✓": it's taken out of both
				// (with the space before it), so "Go Your Own Way ✓" is "Go Your Own Way", tagged ✓.
				if (convert && (name.Contains(Check) || lyrics.Contains(Check)))
				{
					name = WithoutCheck(name).Trim();
					lyrics = WithoutCheck(lyrics);
					tags = SongTags.Tidy($"{tags} {Check}");
				}

				if (lyrics.Length > MaxLyricsLength)
				{
					skipped.Add($"{name} (too long: {lyrics.Length} characters, at most {MaxLyricsLength})");
					continue;
				}

				var tempo = Int(song, "TempoInt");
				var beat = Text(song, "timeSig").Trim();
				var link = Text(song, "Url").Trim();
				songs.Add(new SongbookProSong(
					Int(song, "Id"),
					name.Length > 0 ? name : "Untitled",
					KeyName(Int(song, "key")),
					tempo > 0 ? tempo : 120,
					beat.Length > 0 ? beat : "4/4",
					lyrics,
					link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? link : "",
					TimeSpan.FromSeconds(Math.Max(0, Int(song, "Duration"))),
					tags,
					Artist(song, convert)));
			}

			var sets = new List<SongbookProSet>();
			foreach (var set in (json["sets"] as JsonArray ?? []).OfType<JsonObject>())
			{
				if (set["details"] is not JsonObject details || Bool(details, "Deleted"))
				{
					continue;
				}

				var songIds = (set["contents"] as JsonArray ?? []).OfType<JsonObject>()
					.Where(item => !Bool(item, "Deleted") && Int(item, "ItemType") is 0 or 1)
					// In their "Order"; two with the same number (it happens) in the order they were added.
					.OrderBy(item => Int(item, "Order"))
					.ThenBy(item => Int(item, "Id"))
					.Select(item => Int(item, "SongId"))
					.ToList();
				var setName = Text(details, "name").Trim();
				sets.Add(new SongbookProSet(setName.Length > 0 ? setName : "Imported set", songIds));
			}

			// Some backups number the songs anew (1, 2, …) but leave the sets pointing at the old
			// numbers, the new ones shifted by the number of songs. When the sets' songs fit better
			// shifted, they're read shifted.
			var backupSongIds = (json["songs"] as JsonArray ?? []).OfType<JsonObject>().Select(song => Int(song, "Id")).ToHashSet();
			var setSongIds = sets.SelectMany(set => set.SongIds).ToList();
			int Matches(int shift) => setSongIds.Count(id => backupSongIds.Contains(id - shift));
			var shift = new[] { 0, backupSongIds.Count, backupSongIds.DefaultIfEmpty().Max() }
				.Distinct()
				.OrderByDescending(Matches)
				.ThenBy(candidate => candidate)
				.First();
			if (shift != 0)
			{
				sets = sets.Select(set => set with { SongIds = set.SongIds.Select(id => id - shift).ToList() }).ToList();
			}

			return new SongbookProBackup(songs, sets, skipped);
		}

		/// <summary>
		/// Adds the backup's songs to the band's songbook and its sets as the band's setlists (with the
		/// songs that were imported, in their order). Changes go through the service like any other,
		/// so this works offline too.
		/// </summary>
		public static async Task<SongbookProImportResult> ImportAsync(ISetlistService service, Guid groupId, SongbookProBackup backup)
		{
			var imported = new Dictionary<int, Guid>();
			// The songbook's songs by name, exactly as written (case and spaces count) but for a ✓ in it:
			// a song of the same name is overwritten rather than added again.
			var byName = new Dictionary<string, SongDetails>(StringComparer.Ordinal);
			foreach (var existing in await service.GetSongbookSongsAsync(groupId))
			{
				byName.TryAdd(NameKey(existing.Name), existing);
			}

			var replaced = 0;
			foreach (var song in backup.Songs)
			{
				SongDetails stored;
				if (byName.TryGetValue(NameKey(song.Name), out var existing))
				{
					// The backup's lyrics, key (as both the playing and the original key), tempo and beat; the song keeps its link when the backup
					// has none, and its tags with the backup's added.
					stored = await service.UpdateSongAsync(existing with
					{
						// The backup's name: the same but for a ✓ (which a converted import leaves out).
						Name = song.Name,
						Lyrics = song.Lyrics,
						PlayingKey = song.Key,
						OriginalKey = song.Key,
						Artist = song.Artist,
						Tempo = song.Tempo,
						Beat = song.Beat,
						MediaLink = song.MediaLink.Length > 0 ? song.MediaLink : existing.MediaLink,
						Tags = SongTags.Join(SongTags.Parse(existing.Tags).Concat(SongTags.Parse(song.Tags))),
					});
					replaced++;
				}
				else
				{
					var created = await service.CreateSongAsync(groupId, song.Name, song.Lyrics, song.Duration);
					stored = await service.UpdateSongAsync(created with { PlayingKey = song.Key, OriginalKey = song.Key, Artist = song.Artist, Tempo = song.Tempo, Beat = song.Beat, MediaLink = song.MediaLink, Tags = song.Tags });
				}

				byName[NameKey(song.Name)] = stored;
				imported[song.SourceId] = stored.Id;
			}

			// The band's setlists by name, exactly as written (case and spaces count): a set of the same
			// name replaces that setlist's songs rather than being added as another setlist.
			var setlistsByName = new Dictionary<string, SetlistSummary>(StringComparer.Ordinal);
			foreach (var existing in await service.GetSetlistsAsync(groupId))
			{
				setlistsByName.TryAdd(existing.Name, existing);
			}

			var setlists = 0;
			var replacedSetlists = 0;
			var skipped = backup.Skipped.ToList();
			foreach (var set in backup.Sets)
			{
				var songIds = set.SongIds.Where(imported.ContainsKey).Select(id => imported[id]).ToList();
				if (songIds.Count == 0)
				{
					// Only PDF songs (or none): it would be an empty setlist.
					skipped.Add($"the set {set.Name} (no songs that could be imported)");
					continue;
				}

				if (setlistsByName.TryGetValue(set.Name, out var setlist))
				{
					// Its songs become the set's, in the set's order (it stays the same setlist, so it
					// stays the band's current one if it was).
					await service.ReplaceSongsAsync(setlist.Id, songIds);
					replacedSetlists++;
				}
				else
				{
					setlist = await service.CreateSetlistAsync(groupId, set.Name);
					await service.AddSongsAsync(setlist.Id, songIds);
					setlistsByName[set.Name] = setlist;
				}

				setlists++;
			}

			return new SongbookProImportResult(imported.Count, setlists, skipped, replaced, replacedSetlists);
		}

		/// <summary>
		/// The song's lyrics: its ChordPro content, after a {capo: n} line when it has a capo (its
		/// author is the song's artist, see <see cref="Artist"/>).
		/// Converted to Lyrious' format, the first line is a heading with the key and the name
		/// ("# [D]Amazing Grace"), each comment line "{c: Verse 1}" is a heading "## Verse 1", and a
		/// heading that starts with a chord has it capitalised and right on its text ("# [d] Intro" is "# [D]Intro").
		/// </summary>
		private static string Lyrics(JsonObject song, bool convert)
		{
			var lines = new List<string>();
			if (convert)
			{
				var key = KeyName(Int(song, "key"));
				var name = Text(song, "name").Trim();
				lines.Add(key.Length > 0 ? $"# [{key}] {name}" : $"# {name}");
			}

			if (Int(song, "Capo") is > 0 and var capo)
			{
				lines.Add($"{{capo: {capo}}}");
			}

			var content = Text(song, "content").Replace("\r\n", "\n").Trim();
			if (convert)
			{
				content = CommentLine().Replace(content, match => $"## {match.Groups[1].Value.Trim()}");
			}

			lines.Add(content);
			var lyrics = string.Join('\n', lines).Trim();
			if (convert)
			{
				// A heading that starts with a chord, "# [d] Text", gets the chord capitalised and
				// right on the text, "# [D]Text", so it's played on the heading's first word.
				lyrics = HeadingChord().Replace(lyrics, match =>
				{
					var chord = match.Groups[2].Value;
					return $"{match.Groups[1].Value}[{char.ToUpperInvariant(chord[0])}{chord[1..]}]";
				});
			}

			return lyrics;
		}

		/// <summary>A heading line's chord with the spaces after it, "# [d] ": group 1 is "# ", group 2 the chord.</summary>
		[System.Text.RegularExpressions.GeneratedRegex(@"^([ \t]*#+[ \t]+)\[([^\]\s][^\]]*)\][ \t]+(?=\S)", System.Text.RegularExpressions.RegexOptions.Multiline)]
		private static partial System.Text.RegularExpressions.Regex HeadingChord();

		/// <summary>The mark that, in a name or lyrics, becomes a tag of its own when the import converts.</summary>
		private const string Check = "✓";

		/// <summary>
		/// A song's name as songs of the same name are found by: exactly as written, but without its ✓
		/// (and the space before it), so "Go Your Own Way ✓" and "Go Your Own Way" are the same song.
		/// </summary>
		private static string NameKey(string name) => WithoutCheck(name).Trim();

		/// <summary>The text without its ✓ marks, each with the space or tab before it (if any).</summary>
		private static string WithoutCheck(string text) => CheckMark().Replace(text, "");

		[System.Text.RegularExpressions.GeneratedRegex(@"[ \t]?✓")]
		private static partial System.Text.RegularExpressions.Regex CheckMark();

		/// <summary>Artists whose songs get their name as a tag when the import converts.</summary>
		private static readonly string[] TaggedArtists = ["Maria", "Tobbe", "Robert"];

		/// <summary>
		/// The tags a converted song gets: the artist's name when it's one of <see cref="TaggedArtists"/>
		/// (ignoring case; an artist like "Maria & Tobbe" gives both).
		/// </summary>
		private static string Tags(JsonObject song)
		{
			var artists = AuthorNames(Text(song, "author"));
			return SongTags.Join(TaggedArtists.Where(name => artists.Contains(name, StringComparer.OrdinalIgnoreCase)));
		}

		/// <summary>
		/// The song's artist: its author in the backup. Converted, Maria, Tobbe and Robert are tags
		/// instead (see <see cref="Tags"/>), so they're left out: "Maria" gives no artist, and
		/// "Maria &amp; Elvis Presley" gives "Elvis Presley".
		/// </summary>
		private static string Artist(JsonObject song, bool convert)
		{
			var author = Text(song, "author").Trim();
			if (!convert)
			{
				return author;
			}

			var names = AuthorNames(author);
			var others = names.Where(name => !TaggedArtists.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
			// Nothing of the author is a tag: kept as written (e.g. "Simon & Garfunkel").
			return others.Length == names.Length ? author : string.Join(" & ", others);
		}

		/// <summary>
		/// The names in an author: split at commas, "&amp;", "/" and "+"; a part made only of the tagged
		/// names ("Maria Tobbe") is those names.
		/// </summary>
		private static string[] AuthorNames(string author)
			=> author.Split([',', '&', '/', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.SelectMany(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } words
				                    && words.All(word => TaggedArtists.Contains(word, StringComparer.OrdinalIgnoreCase))
					? words
					: [part])
				.ToArray();

		/// <summary>A ChordPro comment on a line of its own, "{c: Verse 1}": the text is group 1.</summary>
		[System.Text.RegularExpressions.GeneratedRegex(@"^[ \t]*\{c:[ \t]*(.*?)[ \t]*\}[ \t]*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
		private static partial System.Text.RegularExpressions.Regex CommentLine();

		/// <summary>The key's name from SongbookPro's number: 0–11 major from A (5 is D), 12–23 the same minor (22 is Gm).</summary>
		internal static string KeyName(int key) => key switch
		{
			>= 0 and < 12 => KeyNames[key],
			>= 12 and < 24 => KeyNames[key - 12] + "m",
			_ => "",
		};

		private static string Text(JsonObject item, string field) => item[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

		private static int Int(JsonObject item, string field)
		{
			if (item[field] is not JsonValue value)
			{
				return 0;
			}

			return value.TryGetValue<int>(out var number) ? number : value.TryGetValue<double>(out var real) ? (int)real : 0;
		}

		private static bool Bool(JsonObject item, string field)
			=> item[field] is JsonValue value && (value.TryGetValue<bool>(out var flag) ? flag : value.TryGetValue<int>(out var number) && number != 0);
	}
}
