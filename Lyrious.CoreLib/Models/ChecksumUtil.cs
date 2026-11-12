using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace Lyrious.CoreLib.Models;

/// <summary>
/// Content checksums that are stable across processes, machines and runs, so a client and the
/// server compute the same value for the same entity (HashCode is randomly seeded per process).
/// The checksum covers the [DataMember] properties except Checksum itself and ChangedAt: two copies
/// with the same content match even if one was stamped later. Navigation properties and
/// Identity-managed fields aren't [DataMember] and are never part of it.
/// </summary>
public static class ChecksumUtil
{
	private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ContentProperties = new();

	public static bool VerifyChecksum(IEntity entity)
	{
		return entity.Checksum == CalculateChecksum(entity);
	}

	public static long CalculateChecksum(IEntity entity)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (var property in GetContentProperties(entity.GetType()))
		{
			Append(hash, property.Name);
			Append(hash, Format(property.GetValue(entity)));
		}

		Span<byte> digest = stackalloc byte[32];
		hash.GetHashAndReset(digest);
		return BinaryPrimitives.ReadInt64LittleEndian(digest);
	}

	/// <summary>
	/// Call where a change originates: if the content no longer matches the stored checksum,
	/// stamps ChangedAt and stores the new checksum. Returns whether the content had changed.
	/// </summary>
	public static bool UpdateChecksum(IEntity entity)
	{
		var calculatedChecksum = CalculateChecksum(entity);
		if (entity.Checksum == calculatedChecksum)
		{
			return false;
		}

		entity.ChangedAt = DateTime.Now;
		entity.Checksum = calculatedChecksum;
		return true;
	}

	private static PropertyInfo[] GetContentProperties(Type type)
	{
		return ContentProperties.GetOrAdd(type, t => t
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.CanRead
			            && p.GetIndexParameters().Length == 0
			            && Attribute.IsDefined(p, typeof(DataMemberAttribute), true)
			            && p.Name is not (nameof(IEntity.Checksum) or nameof(IEntity.ChangedAt)))
			.OrderBy(p => p.Name, StringComparer.Ordinal)
			.ToArray());
	}

	/// <summary>Culture-independent, type-tagged text for a property value.</summary>
	private static string Format(object? value)
	{
		return value switch
		{
			null => "n:",
			string s => "s:" + s,
			// Ticks only: Kind isn't preserved by SQL Server or SQLite, so it mustn't affect the checksum.
			DateTime d => "d:" + d.Ticks.ToString(CultureInfo.InvariantCulture),
			DateTimeOffset d => "o:" + d.UtcTicks.ToString(CultureInfo.InvariantCulture),
			float f => "f:" + f.ToString("R", CultureInfo.InvariantCulture),
			double d => "f:" + d.ToString("R", CultureInfo.InvariantCulture),
			bool b => b ? "b:1" : "b:0",
			Enum e => "e:" + Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
			IFormattable f => "v:" + f.ToString(null, CultureInfo.InvariantCulture),
			_ => "x:" + value
		};
	}

	// Length-prefixed so adjacent values can't run together ("ab"+"c" vs "a"+"bc").
	private static void Append(IncrementalHash hash, string text)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
		hash.AppendData(length);
		hash.AppendData(bytes);
	}
}
