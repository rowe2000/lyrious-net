using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.Api.Services;

/// <summary>
/// A complete export of Lyrious' data, for site managers: members, bands, memberships, songbooks,
/// songs, setlists and their songs, members' song settings, sessions and plays, and release notes,
/// removed ones too (with their Removed flag). Each table has its rows' plain values (ids, names,
/// texts, numbers, dates; no links between objects). Members are exported without what logs them in
/// (password hash, security stamp and other secrets). As JSON, or as a SQLite database of the same
/// tables.
/// </summary>
public static class DataExport
{
	/// <summary>A table of the export: its name and its rows, each its columns by name in order.</summary>
	public sealed record Table(string Name, IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);

	/// <summary>A member's columns in the export: never their password hash, security stamp or other login secrets.</summary>
	private static readonly string[] MemberColumns =
	[
		nameof(Member.Id), nameof(Member.UserName), nameof(Member.Name), nameof(Member.Email), nameof(Member.EmailConfirmed),
		nameof(Member.JoinedGroupId), nameof(Member.LastLoginAt), nameof(Member.LockoutEnd), nameof(Member.Settings),
		nameof(Member.CreatedAt), nameof(Member.ChangedAt), nameof(Member.Removed),
	];

	/// <summary>Every table of the export, read from the database.</summary>
	public static IReadOnlyList<Table> Tables(LyriousContext context) =>
	[
		Of("members", context.Members.AsNoTracking().ToList(), MemberColumns),
		Of("bands", context.Groups.AsNoTracking().ToList()),
		Of("memberships", context.Memberships.AsNoTracking().ToList()),
		Of("songbooks", context.Songbooks.AsNoTracking().ToList()),
		Of("songs", context.Songs.AsNoTracking().ToList()),
		Of("setlists", context.Setlists.AsNoTracking().ToList()),
		Of("setlist_items", context.SetlistItems.AsNoTracking().ToList()),
		Of("song_settings", context.SongSettings.AsNoTracking().ToList()),
		Of("sessions", context.Playlogs.AsNoTracking().ToList()),
		Of("plays", context.Plays.AsNoTracking().ToList()),
		Of("release_notes", context.ReleaseNotes.AsNoTracking().ToList()),
	];

	/// <summary>
	/// A table of the items: their plain values (text, numbers, true/false, ids, dates, enums), not the
	/// objects they link to; only <paramref name="only"/> when given (in that order).
	/// </summary>
	private static Table Of<T>(string name, IReadOnlyList<T> items, string[]? only = null)
	{
		var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.CanRead && property.GetIndexParameters().Length == 0 && IsPlain(property.PropertyType))
			.ToList();
		if (only != null)
		{
			properties = only.Select(column => properties.First(property => property.Name == column)).ToList();
		}
		else
		{
			// The id first, the rest as declared.
			properties = properties.OrderBy(property => property.Name == "Id" ? 0 : 1).ToList();
		}

		return new Table(
			name,
			properties.Select(property => property.Name).ToList(),
			items.Select(item => properties.Select(property => property.GetValue(item)).ToArray()).ToList());
	}

	/// <summary>Whether a property holds a plain value (exported), not another object or a list (left out).</summary>
	private static bool IsPlain(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime)
		       || type == typeof(DateTimeOffset) || type == typeof(decimal) || type == typeof(TimeSpan);
	}

	/// <summary>The export as JSON: when and what, then each table as a list of objects.</summary>
	public static byte[] Json(IReadOnlyList<Table> tables)
	{
		var json = new JsonObject
		{
			["exportedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
			["format"] = "Lyrious data export 1",
		};
		var all = new JsonObject();
		foreach (var table in tables)
		{
			var rows = new JsonArray();
			foreach (var row in table.Rows)
			{
				var item = new JsonObject();
				for (var i = 0; i < table.Columns.Count; i++)
				{
					item[Camel(table.Columns[i])] = JsonValueOf(row[i]);
				}

				rows.Add(item);
			}

			all[table.Name] = rows;
		}

		json["tables"] = all;
		return JsonSerializer.SerializeToUtf8Bytes(json, new JsonSerializerOptions { WriteIndented = true });
	}

	/// <summary>The export as a SQLite database: each table with its columns (text, whole numbers or real numbers).</summary>
	public static byte[] Sqlite(IReadOnlyList<Table> tables)
	{
		var file = Path.Combine(Path.GetTempPath(), $"lyrious-export-{Guid.NewGuid():N}.db");
		try
		{
			using (var db = new SqliteConnection($"Data Source={file};Pooling=False"))
			{
				db.Open();
				using var transaction = db.BeginTransaction();
				foreach (var table in tables)
				{
					var columns = table.Columns.Select((column, i) => $"\"{column}\" {SqlType(table, i)}");
					using (var create = db.CreateCommand())
					{
						create.CommandText = $"CREATE TABLE \"{table.Name}\" ({string.Join(", ", columns)})";
						create.ExecuteNonQuery();
					}

					using var insert = db.CreateCommand();
					insert.CommandText = $"INSERT INTO \"{table.Name}\" VALUES ({string.Join(", ", table.Columns.Select((_, i) => $"$p{i}"))})";
					var parameters = table.Columns.Select((_, i) => insert.Parameters.Add($"$p{i}", SqliteType.Text)).ToArray();
					foreach (var row in table.Rows)
					{
						for (var i = 0; i < row.Length; i++)
						{
							parameters[i].Value = SqlValue(row[i]) ?? DBNull.Value;
						}

						insert.ExecuteNonQuery();
					}
				}

				transaction.Commit();
			}

			return File.ReadAllBytes(file);
		}
		finally
		{
			File.Delete(file);
		}
	}

	/// <summary>A column's SQLite type: whole numbers (and true/false, enums) INTEGER, real numbers REAL, the rest TEXT.</summary>
	private static string SqlType(Table table, int column)
	{
		var sample = table.Rows.Select(row => row[column]).FirstOrDefault(value => value != null);
		return sample switch
		{
			bool or byte or short or int or long or Enum => "INTEGER",
			float or double or decimal => "REAL",
			_ => "TEXT",
		};
	}

	/// <summary>A value as SQLite stores it: whole numbers, real numbers or text (ids and dates as text).</summary>
	private static object? SqlValue(object? value) => value switch
	{
		null => null,
		bool flag => flag ? 1L : 0L,
		Enum choice => Convert.ToInt64(choice, CultureInfo.InvariantCulture),
		byte or short or int or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
		float or double or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
		DateTime time => time.ToString("o", CultureInfo.InvariantCulture),
		DateTimeOffset time => time.ToString("o", CultureInfo.InvariantCulture),
		_ => Convert.ToString(value, CultureInfo.InvariantCulture),
	};

	/// <summary>A value in the JSON export: numbers, true/false and text as such, enums as their number, ids and dates as text.</summary>
	private static JsonNode? JsonValueOf(object? value) => SqlValue(value) switch
	{
		null => null,
		long when value is bool flag => JsonValue.Create(flag),
		long number => JsonValue.Create(number),
		double real => JsonValue.Create(real),
		string text => JsonValue.Create(text),
		_ => null,
	};

	/// <summary>A property's name as the JSON names fields: "CreatedAt" is "createdAt".</summary>
	private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
