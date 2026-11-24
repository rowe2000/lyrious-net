using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Reads the rows of tables in a SQLite database file (e.g. SongbookPro's songs.db), read-only and
	/// in plain code, so it works on every platform the app runs on (the browser too). It follows the
	/// SQLite file format: the tables' B-trees, their records and overflow pages. Each row is its
	/// columns by name (an INTEGER PRIMARY KEY column holds the row's id); a column added to the table
	/// after a row was written has its DEFAULT value in that row (else null).
	/// </summary>
	internal sealed class SqliteFile
	{
		private readonly byte[] data;
		private readonly int pageSize;
		// The bytes of a page that records use (the page less the room reserved at its end).
		private readonly int usable;
		private readonly Encoding text;

		/// <summary>Whether the bytes start as a SQLite database file does.</summary>
		public static bool IsSqlite(byte[] data)
			=> data.Length >= 100 && Encoding.ASCII.GetString(data, 0, 16) == "SQLite format 3\0";

		/// <summary>Opens the database; throws <see cref="InvalidOperationException"/> when it isn't one.</summary>
		public SqliteFile(byte[] data)
		{
			if (!IsSqlite(data))
			{
				throw new InvalidOperationException("This isn't a SQLite database.");
			}

			this.data = data;
			var size = (data[16] << 8) | data[17];
			pageSize = size == 1 ? 65536 : size;
			usable = pageSize - data[20];
			text = ReadUInt32(56) switch
			{
				2 => Encoding.Unicode,
				3 => Encoding.BigEndianUnicode,
				_ => Encoding.UTF8,
			};
		}

		/// <summary>The rows of the table, each its columns by name; none when there's no such table.</summary>
		public IReadOnlyList<Dictionary<string, object?>> Rows(string table)
		{
			// The tables' names, root pages and definitions, in sqlite_master (on page 1).
			var master = new List<(long RowId, object?[] Values)>();
			Walk(1, master);
			foreach (var (_, values) in master)
			{
				if (values.Length >= 5 && values[0] as string == "table" && string.Equals(values[1] as string, table, StringComparison.OrdinalIgnoreCase)
				    && values[3] is long root && values[4] is string sql)
				{
					var (columns, rowIdColumn, defaults) = Columns(sql);
					var rows = new List<(long RowId, object?[] Values)>();
					Walk((int)root, rows);
					return rows.Select(row =>
					{
						var named = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
						for (var i = 0; i < columns.Count; i++)
						{
							named[columns[i]] = i == rowIdColumn ? row.RowId : i < row.Values.Length ? row.Values[i] : defaults[i];
						}

						return named;
					}).ToList();
				}
			}

			return [];
		}

		/// <summary>
		/// The table's column names, in order, from its CREATE TABLE statement; which one (if any) is the
		/// row's id (INTEGER PRIMARY KEY: its value isn't stored in the record); and each one's DEFAULT.
		/// </summary>
		private static (List<string> Columns, int RowIdColumn, List<object?> Defaults) Columns(string sql)
		{
			var columns = new List<string>();
			var defaults = new List<object?>();
			var rowIdColumn = -1;
			var start = sql.IndexOf('(');
			var end = sql.LastIndexOf(')');
			if (start < 0 || end <= start)
			{
				return (columns, rowIdColumn, defaults);
			}

			// The definitions, split at the commas that aren't inside brackets or quotes.
			var parts = new List<string>();
			var depth = 0;
			var quote = '\0';
			var from = start + 1;
			for (var i = start + 1; i < end; i++)
			{
				var c = sql[i];
				if (quote != '\0')
				{
					quote = c == quote ? '\0' : quote;
				}
				else if (c is '"' or '\'' or '`' or '[')
				{
					quote = c == '[' ? ']' : c;
				}
				else if (c == '(')
				{
					depth++;
				}
				else if (c == ')')
				{
					depth--;
				}
				else if (c == ',' && depth == 0)
				{
					parts.Add(sql[from..i]);
					from = i + 1;
				}
			}

			parts.Add(sql[from..end]);
			foreach (var part in parts.Select(p => p.Trim()).Where(p => p.Length > 0))
			{
				var first = part.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
				if (first.ToUpperInvariant() is "CONSTRAINT" or "PRIMARY" or "UNIQUE" or "CHECK" or "FOREIGN")
				{
					continue; // A table constraint, not a column.
				}

				var name = first.Trim('"', '`', '[', ']', '\'');
				var rest = part[first.Length..].ToUpperInvariant();
				if (rest.TrimStart().StartsWith("INTEGER", StringComparison.Ordinal) && rest.Contains("PRIMARY KEY", StringComparison.Ordinal))
				{
					rowIdColumn = columns.Count;
				}

				columns.Add(name);
				defaults.Add(Default(part[first.Length..]));
			}

			return (columns, rowIdColumn, defaults);
		}

		/// <summary>A column definition's DEFAULT value: a number, 'text', x'blob' or NULL; null when it has none.</summary>
		private static object? Default(string definition)
		{
			var match = System.Text.RegularExpressions.Regex.Match(definition, @"\bDEFAULT\s+(x'(?<blob>[0-9a-fA-F]*)'|'(?<text>(?:''|[^'])*)'|(?<number>[-+]?[0-9.]+(?:[eE][-+]?[0-9]+)?))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			if (!match.Success)
			{
				return null;
			}

			if (match.Groups["blob"].Success)
			{
				return Convert.FromHexString(match.Groups["blob"].Value);
			}

			if (match.Groups["text"].Success)
			{
				return match.Groups["text"].Value.Replace("''", "'");
			}

			var number = match.Groups["number"].Value;
			// Each its own type (a ?: would make both double).
			if (long.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var whole))
			{
				return whole;
			}

			return double.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>Adds the rows of the table B-tree from this page down, in order.</summary>
		private void Walk(int page, List<(long RowId, object?[] Values)> rows)
		{
			var offset = (page - 1) * pageSize;
			var header = page == 1 ? offset + 100 : offset;
			var type = data[header];
			var cells = ReadUInt16(header + 3);
			if (type == 0x05)
			{
				// An interior page: each cell points at the page of the rows before its key; the
				// right-most pointer at the page of those after the last one.
				for (var i = 0; i < cells; i++)
				{
					var cell = offset + ReadUInt16(header + 12 + i * 2);
					Walk((int)ReadUInt32(cell), rows);
				}

				Walk((int)ReadUInt32(header + 8), rows);
			}
			else if (type == 0x0D)
			{
				for (var i = 0; i < cells; i++)
				{
					var cell = offset + ReadUInt16(header + 8 + i * 2);
					var payloadSize = ReadVarint(ref cell);
					var rowId = ReadVarint(ref cell);
					rows.Add((rowId, Record(Payload(cell, payloadSize))));
				}
			}
		}

		/// <summary>A cell's payload: its part on the page, and the rest from its overflow pages.</summary>
		private byte[] Payload(int at, long size)
		{
			var maxLocal = usable - 35;
			if (size <= maxLocal)
			{
				return data[at..(at + (int)size)];
			}

			var minLocal = (usable - 12) * 32 / 255 - 23;
			var local = minLocal + (int)((size - minLocal) % (usable - 4));
			if (local > maxLocal)
			{
				local = minLocal;
			}

			var payload = new byte[size];
			Array.Copy(data, at, payload, 0, local);
			var written = local;
			var next = ReadUInt32(at + local);
			while (written < size && next != 0)
			{
				var page = ((int)next - 1) * pageSize;
				next = ReadUInt32(page);
				var take = (int)Math.Min(usable - 4, size - written);
				Array.Copy(data, page + 4, payload, written, take);
				written += take;
			}

			return payload;
		}

		/// <summary>A record's values: null, a number (long or double), text (string) or a blob (byte[]).</summary>
		private object?[] Record(byte[] record)
		{
			var at = 0;
			var headerSize = ReadVarint(record, ref at);
			var types = new List<long>();
			while (at < headerSize)
			{
				types.Add(ReadVarint(record, ref at));
			}

			var values = new object?[types.Count];
			var body = (int)headerSize;
			for (var i = 0; i < types.Count; i++)
			{
				var serial = types[i];
				switch (serial)
				{
					case 0:
						values[i] = null;
						break;
					case >= 1 and <= 6:
						var length = serial switch { 5 => 6, 6 => 8, _ => (int)serial == 4 ? 4 : (int)serial };
						long number = (sbyte)record[body];
						for (var b = 1; b < length; b++)
						{
							number = (number << 8) | record[body + b];
						}

						values[i] = number;
						body += length;
						break;
					case 7:
						var bits = 0L;
						for (var b = 0; b < 8; b++)
						{
							bits = (bits << 8) | record[body + b];
						}

						values[i] = BitConverter.Int64BitsToDouble(bits);
						body += 8;
						break;
					case 8:
						values[i] = 0L;
						break;
					case 9:
						values[i] = 1L;
						break;
					case >= 12 when serial % 2 == 0:
						var blob = (int)(serial - 12) / 2;
						values[i] = record[body..(body + blob)];
						body += blob;
						break;
					case >= 13:
						var chars = (int)(serial - 13) / 2;
						values[i] = text.GetString(record, body, chars);
						body += chars;
						break;
				}
			}

			return values;
		}

		private int ReadUInt16(int at) => (data[at] << 8) | data[at + 1];

		private uint ReadUInt32(int at) => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);

		private long ReadVarint(ref int at) => ReadVarint(data, ref at);

		/// <summary>A SQLite varint: 7 bits a byte, high bit set while more follow; a ninth byte gives all 8 bits.</summary>
		private static long ReadVarint(byte[] bytes, ref int at)
		{
			long value = 0;
			for (var i = 0; i < 9; i++)
			{
				var b = bytes[at++];
				if (i == 8)
				{
					return (value << 8) | b;
				}

				value = (value << 7) | (long)(b & 0x7f);
				if ((b & 0x80) == 0)
				{
					return value;
				}
			}

			return value;
		}
	}
}
