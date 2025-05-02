using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Lyrious.CoreLib.Models;

public interface IEntity
{
	[Key][DataMember] Guid Id { get; }
	[DataMember] DateTime CreatedAt { get; set; }
	[DataMember] DateTime ChangedAt { get; set; }
	[DataMember] bool Removed { get; set; }
	[DataMember] long Checksum { get; set; }

	public T Clone<T>() where T : new()
	{
		var clone = new T();

		typeof(T).GetProperties()
			.Where(p => p.GetCustomAttributes(typeof(DataMemberAttribute), true).Any())
			.ToList()
			.ForEach(property =>
			{
				var value = property.GetValue(this);
				property.SetValue(clone, value);
			});

		return clone;
	}


}