using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Lyrious.CoreLib.Enums;
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

    /// <summary>When the member last logged in or opened the app connected to the server; null if never since this was recorded.</summary>
    [DataMember] public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// What other members may see of a member: name, joined group and last login. Identity fields
    /// (password hash, security stamp, email, phone) stay on the server.
    /// </summary>
    public Member ToPublicProfile() => new()
    {
	    Id = Id,
	    UserName = UserName,
	    Name = Name,
	    JoinedGroupId = JoinedGroupId,
	    LastLoginAt = LastLoginAt,
	    CreatedAt = CreatedAt,
	    ChangedAt = ChangedAt,
	    Removed = Removed,
	    Checksum = Checksum,
    };

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
    public static Member Create(string userName, string name, string? email, string? phone)
    {
	    return new Member
	    {
		    UserName = userName,
		    Name = name,
		    Email = email,
		    PhoneNumber = phone
	    };
    }

    public bool Join(Group group)
    {
	    if (JoinedGroupId == group.Id)
	    {
		    return false;
	    }

	    if (!Memberships.Any(o => o.GroupId == group.Id && o.MembershipStatusEnum == MembershipStatusEnum.Approved))
	    {
		    return false;
	    }

	    if (JoinedGroup != null)
	    {
		    Leave(JoinedGroup);
	    }

	    JoinedGroup = group;
	    JoinedGroupId = group.Id;

	    if (!group.JoinedMembers.Contains(this))
	    {
		    group.JoinedMembers.Add(this);
	    }

	    return true;

    }

    public bool Leave(Group group)
    {
	    JoinedGroup = null;
	    JoinedGroupId = null;
	    return group.JoinedMembers.Remove(this);
    }
}