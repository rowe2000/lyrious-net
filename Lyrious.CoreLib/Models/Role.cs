using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Lyrious.CoreLib.Models;

public sealed class Role : IdentityRole<Guid>, IEntity
{
	[Key][DataMember] public override Guid Id { get; set; } = Guid.NewGuid();
	[DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
	[DataMember] public DateTime ChangedAt { get; set; } = DateTime.Now;
	[DataMember] public bool Removed { get; set; }
	public long Checksum { get; set; }
}