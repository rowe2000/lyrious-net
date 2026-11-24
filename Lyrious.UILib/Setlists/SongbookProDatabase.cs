using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Lyrious.UILib.Setlists
{
	/// <summary>A band in SongbookPro's songs.db (a group): its name, and its library's songs and sets.</summary>
	public sealed record SongbookProBand(string Name, SongbookProBackup Backup);

	/// <summary>What songs.db holds: the member's own songs and sets, and each band's (group's).</summary>
	public sealed record SongbookProDatabase(SongbookProBackup Private, IReadOnlyList<SongbookProBand> Bands);

	/// <summary>How importing songs.db went: into the private songbook, and into each band (and whether that band was made for it).</summary>
	public sealed record SongbookProDatabaseResult(SongbookProImportResult Private, IReadOnlyList<(string Band, bool Created, SongbookProImportResult Result)> Bands);

	public static partial class SongbookProImport
	{
		/// <summary>Whether the file is a database (SongbookPro's songs.db) rather than a backup.</summary>
		public static bool IsDatabase(byte[] data) => SqliteFile.IsSqlite(data);

		/// <summary>
		/// Reads SongbookPro's own database, songs.db (a SQLite file), into what can be imported, the
		/// same way as a backup (see <see cref="Read"/>), converted with the same options:
		/// <list type="bullet">
		/// <item>Its Songs, and its Sets with their SetItems, are the member's own: for their private songbook.</item>
		/// <item>Each group (groups_orgs) is a band: the songs of its library (groups_libraries), each with
		/// its default version's lyrics and key (groups_songs, groups_song_variants), and its sets with their
		/// songs (groups_sets, groups_set_items). These are kept as MessagePack in songs.db.</item>
		/// </list>
		/// Throws <see cref="InvalidOperationException"/> when the file isn't a SongbookPro database.
		/// </summary>
		public static SongbookProDatabase ReadDatabase(byte[] data, SongbookProImportOptions? options = null)
		{
			options ??= new SongbookProImportOptions(CommentsAsHeadings: false, HeadingChords: false, CheckTags: false);
			var db = new SqliteFile(data);
			var songRows = db.Rows("Songs");
			if (songRows.Count == 0 && db.Rows("groups_songs").Count == 0)
			{
				throw new InvalidOperationException(Localization.L.T("This isn't a SongbookPro database (it has no songs)."));
			}

			// The member's own: the rows as a backup has them (its fields are songs.db's columns).
			var setItems = db.Rows("SetItems").Select(Json).ToList();
			var own = new JsonObject
			{
				["songs"] = new JsonArray(songRows.Select(row =>
				{
					var song = Json(row);
					song["sbpTags"] = TagsOf(row.GetValueOrDefault("_tags") as string);
					return (JsonNode)song;
				}).ToArray()),
				["sets"] = new JsonArray(db.Rows("Sets").Select(row =>
				{
					var details = Json(row);
					var id = Number(row.GetValueOrDefault("Id"));
					var contents = setItems.Where(item => Number(item["SetId"]?.GetValue<int>()) == id).Select(item => item.DeepClone()).ToArray();
					return (JsonNode)new JsonObject { ["details"] = details, ["contents"] = new JsonArray(contents) };
				}).ToArray()),
			};
			var privateBackup = FromJson(own, options);

			// Each band (group): its library's songs and sets.
			var libraries = db.Rows("groups_libraries");
			var groupSongs = db.Rows("groups_songs");
			var variants = db.Rows("groups_song_variants")
				.GroupBy(row => (row.GetValueOrDefault("id") as string ?? "", row.GetValueOrDefault("library") as string ?? ""))
				.ToDictionary(group => group.Key, group => Map(group.First().GetValueOrDefault("r")));
			var groupSets = db.Rows("groups_sets");
			var groupSetItems = db.Rows("groups_set_items");
			var bands = new List<SongbookProBand>();
			foreach (var org in db.Rows("groups_orgs"))
			{
				var name = (org.GetValueOrDefault("name") as string ?? "").Trim();
				var orgId = org.GetValueOrDefault("id") as string;
				var libraryIds = libraries.Where(library => library.GetValueOrDefault("orgId") as string == orgId)
					.Select(library => library.GetValueOrDefault("id") as string ?? "")
					.ToHashSet();

				// The songs, numbered as a backup numbers them (for the sets to point at).
				var numbers = new Dictionary<string, int>();
				var songs = new JsonArray();
				foreach (var row in groupSongs.Where(row => libraryIds.Contains(row.GetValueOrDefault("library") as string ?? "")))
				{
					var id = row.GetValueOrDefault("id") as string ?? "";
					var library = row.GetValueOrDefault("library") as string ?? "";
					var song = Map(row.GetValueOrDefault("r"));
					// Its default version (else its first): the lyrics and key.
					var versions = (song.GetValueOrDefault("variants") as List<object?> ?? []).OfType<Dictionary<string, object?>>().ToList();
					var chosen = versions.FirstOrDefault(version => version.GetValueOrDefault("default") is true) ?? versions.FirstOrDefault();
					var version = chosen?.GetValueOrDefault("id") is string versionId && variants.TryGetValue((versionId, library), out var found) ? found : [];
					numbers[id] = numbers.Count + 1;
					songs.Add(new JsonObject
					{
						["Id"] = numbers[id],
						["name"] = song.GetValueOrDefault("title") as string ?? "",
						["author"] = song.GetValueOrDefault("artist") as string ?? "",
						["content"] = version.GetValueOrDefault("content") as string ?? "",
						["key"] = Number(version.GetValueOrDefault("key") ?? chosen?.GetValueOrDefault("key")),
						["type"] = Number(version.GetValueOrDefault("type") ?? chosen?.GetValueOrDefault("type") ?? 1L),
						["TempoInt"] = Number(song.GetValueOrDefault("tempo")),
						["timeSig"] = song.GetValueOrDefault("timeSig") as string ?? "",
						["Url"] = song.GetValueOrDefault("url") as string ?? "",
						["Duration"] = Number(song.GetValueOrDefault("duration")),
						["sbpTags"] = new JsonArray((song.GetValueOrDefault("tags") as List<object?> ?? []).OfType<string>().Select(tag => (JsonNode)JsonValue.Create(tag)!).ToArray()),
					});
				}

				// The sets, each with its songs in order.
				var sets = new JsonArray();
				foreach (var row in groupSets.Where(row => libraryIds.Contains(row.GetValueOrDefault("library") as string ?? "")))
				{
					var setId = row.GetValueOrDefault("id") as string;
					var set = Map(row.GetValueOrDefault("r"));
					var items = groupSetItems
						.Where(item => item.GetValueOrDefault("setId") as string == setId)
						.Select((item, index) => (Index: index, Item: Map(item.GetValueOrDefault("r"))))
						.Where(entry => entry.Item.GetValueOrDefault("song") is string song && numbers.ContainsKey(song))
						.Select(entry => (JsonNode)new JsonObject
						{
							["Id"] = entry.Index,
							["SongId"] = numbers[(string)entry.Item["song"]!],
							["Order"] = Number(entry.Item.GetValueOrDefault("order")),
							["ItemType"] = Number(entry.Item.GetValueOrDefault("type") ?? 1L),
						})
						.ToArray();
					sets.Add(new JsonObject
					{
						["details"] = new JsonObject { ["name"] = set.GetValueOrDefault("name") as string ?? "" },
						["contents"] = new JsonArray(items),
					});
				}

				bands.Add(new SongbookProBand(name.Length > 0 ? name : "SongbookPro band", FromJson(new JsonObject { ["songs"] = songs, ["sets"] = sets }, options)));
			}

			return new SongbookProDatabase(privateBackup, bands);
		}

		/// <summary>
		/// Imports what songs.db holds (see <see cref="ReadDatabase"/>): the member's own songs and sets
		/// into their private songbook, and each band's into the band of that name they're in, which is
		/// made (with them in it) when they're in none.
		/// </summary>
		public static async Task<SongbookProDatabaseResult> ImportDatabaseAsync(ISetlistService service, SongbookProDatabase database, SongbookProImportOptions? options = null)
		{
			var groups = await service.GetGroupsAsync();
			var privateGroup = groups.FirstOrDefault(group => group.IsPrivate)
				?? throw new InvalidOperationException(Localization.L.T("Your private songbook couldn't be found."));
			var privateResult = await ImportAsync(service, privateGroup.Id, database.Private, options);

			var bands = new List<(string, bool, SongbookProImportResult)>();
			foreach (var band in database.Bands)
			{
				var group = groups.FirstOrDefault(group => !group.IsPrivate && string.Equals(group.Name.Trim(), band.Name, StringComparison.CurrentCultureIgnoreCase));
				var created = group == null;
				group ??= await service.CreateGroupAsync(band.Name);
				bands.Add((group.Name, created, await ImportAsync(service, group.Id, band.Backup, options)));
			}

			return new SongbookProDatabaseResult(privateResult, bands);
		}

		/// <summary>A songs.db row as a backup's JSON object: its columns, numbers as numbers and text as text (blobs left out).</summary>
		private static JsonObject Json(Dictionary<string, object?> row)
		{
			var json = new JsonObject();
			foreach (var (column, value) in row)
			{
				json[column] = value switch
				{
					long number when number is >= int.MinValue and <= int.MaxValue => JsonValue.Create((int)number),
					long number => JsonValue.Create(number),
					double real => JsonValue.Create(real),
					string text => JsonValue.Create(text),
					_ => null,
				};
			}

			return json;
		}

		/// <summary>A MessagePack column (see <see cref="MessagePack"/>) as its map; empty when it isn't one.</summary>
		private static Dictionary<string, object?> Map(object? blob)
			=> blob is byte[] bytes && MessagePack.Read(bytes) is Dictionary<string, object?> map ? map : [];

		/// <summary>A whole number from songs.db (stored as long or double); 0 when there's none.</summary>
		private static int Number(object? value) => value switch
		{
			long number => (int)Math.Clamp(number, int.MinValue, int.MaxValue),
			int number => number,
			double real => (int)real,
			_ => 0,
		};

		/// <summary>The tags of a songs.db song (its "_tags" column, a JSON list), as a JSON array.</summary>
		private static JsonArray TagsOf(string? text)
		{
			try
			{
				return text != null && JsonNode.Parse(text) is JsonArray tags ? new JsonArray(tags.Select(tag => tag?.DeepClone()).ToArray()) : [];
			}
			catch (System.Text.Json.JsonException)
			{
				return [];
			}
		}
	}
}
