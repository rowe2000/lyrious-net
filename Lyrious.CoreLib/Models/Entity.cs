using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Lyrious.CoreLib.Models;

public abstract class Entity : IEntity
{
	[Key][DataMember] public Guid Id { get; set; } = Guid.NewGuid();
	[DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
	[DataMember] public DateTime ChangedAt { get; set; } = DateTime.Now;
	[DataMember] public bool Removed { get; set; }
	public long Checksum { get; set; }

	public override bool Equals(object? obj)
	{
		if (obj is null)
		{
			return false;
		}

		if (ReferenceEquals(this, obj))
		{
			return true;
		}

		if (obj.GetType() != GetType())
		{
			return false;
		}

		return obj is IEntity entity && entity.Id == Id;
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}

	public override string ToString()
	{
		var name = (this as IName)?.Name;
		return $"{(string.IsNullOrWhiteSpace(name) ? "" : name + ", ")} {Id}";
	}
}