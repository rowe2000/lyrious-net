using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Reads MessagePack data (as SongbookPro keeps its band libraries' songs and sets in songs.db):
	/// maps (string keys), arrays, text, numbers (long or double), true/false, nil and binary.
	/// </summary>
	internal static class MessagePack
	{
		/// <summary>The value the bytes hold: a dictionary, list, string, long, double, bool, byte[] or null.</summary>
		public static object? Read(byte[] data)
		{
			var at = 0;
			return Read(data, ref at);
		}

		private static object? Read(byte[] d, ref int p)
		{
			var b = d[p++];
			if (b <= 0x7f)
			{
				return (long)b;
			}

			if (b >= 0xe0)
			{
				return (long)(sbyte)b;
			}

			switch (b & 0xf0)
			{
				case 0x80:
					return Map(d, ref p, b & 0x0f);
				case 0x90:
					return List(d, ref p, b & 0x0f);
			}

			if ((b & 0xe0) == 0xa0)
			{
				return Text(d, ref p, b & 0x1f);
			}

			return b switch
			{
				0xc0 => null,
				0xc2 => false,
				0xc3 => true,
				0xc4 => Bytes(d, ref p, d[p++]),
				0xc5 => Bytes(d, ref p, (int)Unsigned(d, ref p, 2)),
				0xc6 => Bytes(d, ref p, (int)Unsigned(d, ref p, 4)),
				0xca => (double)BitConverter.Int32BitsToSingle((int)Unsigned(d, ref p, 4)),
				0xcb => BitConverter.Int64BitsToDouble((long)Unsigned(d, ref p, 8)),
				0xcc => (long)Unsigned(d, ref p, 1),
				0xcd => (long)Unsigned(d, ref p, 2),
				0xce => (long)Unsigned(d, ref p, 4),
				0xcf => (long)Unsigned(d, ref p, 8),
				0xd0 => (long)(sbyte)Unsigned(d, ref p, 1),
				0xd1 => (long)(short)Unsigned(d, ref p, 2),
				0xd2 => (long)(int)Unsigned(d, ref p, 4),
				0xd3 => (long)Unsigned(d, ref p, 8),
				0xd9 => Text(d, ref p, d[p++]),
				0xda => Text(d, ref p, (int)Unsigned(d, ref p, 2)),
				0xdb => Text(d, ref p, (int)Unsigned(d, ref p, 4)),
				0xdc => List(d, ref p, (int)Unsigned(d, ref p, 2)),
				0xdd => List(d, ref p, (int)Unsigned(d, ref p, 4)),
				0xde => Map(d, ref p, (int)Unsigned(d, ref p, 2)),
				0xdf => Map(d, ref p, (int)Unsigned(d, ref p, 4)),
				_ => throw new InvalidDataException($"Unknown MessagePack value 0x{b:x2}."),
			};
		}

		/// <summary>A big-endian number of so many bytes.</summary>
		private static ulong Unsigned(byte[] d, ref int p, int length)
		{
			ulong value = 0;
			for (var i = 0; i < length; i++)
			{
				value = (value << 8) | d[p++];
			}

			return value;
		}

		private static string Text(byte[] d, ref int p, int length)
		{
			var text = Encoding.UTF8.GetString(d, p, length);
			p += length;
			return text;
		}

		private static byte[] Bytes(byte[] d, ref int p, int length)
		{
			var bytes = d[p..(p + length)];
			p += length;
			return bytes;
		}

		private static List<object?> List(byte[] d, ref int p, int count)
		{
			var list = new List<object?>(count);
			for (var i = 0; i < count; i++)
			{
				list.Add(Read(d, ref p));
			}

			return list;
		}

		private static Dictionary<string, object?> Map(byte[] d, ref int p, int count)
		{
			var map = new Dictionary<string, object?>(count);
			for (var i = 0; i < count; i++)
			{
				var key = Read(d, ref p)?.ToString() ?? "";
				map[key] = Read(d, ref p);
			}

			return map;
		}
	}
}
