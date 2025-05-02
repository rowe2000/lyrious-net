using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib.Models;

public class Membership : Entity
{
    [DataMember] public RoleEnum RoleEnum { get; set; }
    [DataMember] public MembershipStatusEnum MembershipStatusEnum { get; set; }

    [DataMember] public Guid GroupId { get; set; }
    [DataMember] public Guid MemberId { get; set; }

    [JsonIgnore] public Group? Group { get; set; }
    [JsonIgnore] public Member? Member { get; set; }

    public override string ToString()
    {
	    return $"{Member?.Name ?? ""} @ {Group?.Name} , {RoleEnum} , {MembershipStatusEnum}";
    }

    public static Membership Create(Member member, Group group, RoleEnum role = default, MembershipStatusEnum membershipStatusEnum = default)
    {
	    return new Membership
		{
			Member = member,
			MemberId = member.Id,
			Group = group,
			GroupId = group.Id,
            
			RoleEnum = role,
			MembershipStatusEnum = membershipStatusEnum
		};
    }
}