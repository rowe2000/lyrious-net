using Lyrious.CoreLib.Attributes;
using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib.Models;

public class Membership : EntityBase
{
    [Member] public Role Role { get; set; }
    [Member] public MembershipStatus MembershipStatus { get; set; }

    [Member] public Guid GroupId { get; set; }
    [Member] public Guid MemberId { get; set; }

    public Group Group { get; set; }
    public Member Member { get; set; }


    public static Membership Create(Member member, Group group, Role role = default,
        MembershipStatus membershipStatus = default)
    {
        return new Membership
        {
            Member = member,
            Group = group,
            Role = role,
            MembershipStatus = membershipStatus
        };
    }
}