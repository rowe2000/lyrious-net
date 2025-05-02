using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Lyrious.CoreLib.Models;

public sealed class Member : IdentityUser<Guid>, IEntity, IName
{
    [Key][DataMember] public override Guid Id { get; set; } = Guid.NewGuid();
    [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
	[DataMember] public DateTime ChangedAt { get; set; } = DateTime.Now;
	[DataMember] public bool Removed { get; set; }
	public long Checksum { get; set; }

	[MaxLength(100)]
	[DataMember] public string Name { get; set; } = "";

    [DataMember] public Guid? JoinedGroupId { get; set; }
    [JsonIgnore] public Group? JoinedGroup { get; set; }

	[JsonIgnore] public ObservableList<Membership> Memberships { get; set; } = [];


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
	    return $"{(string.IsNullOrWhiteSpace(Name) ? "" : Name + ", ")} {Id}";
    }
    public static Member Create(string userName, string name, string email, string phone)
    {
	    return new Member
	    {
		    UserName = userName,
		    Name = name,
		    Email = email,
		    PhoneNumber = phone
	    };
    }
}