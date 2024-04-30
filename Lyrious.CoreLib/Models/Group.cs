using Lyrious.CoreLib.Attributes;
using Lyrious.CoreLib.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lyrious.CoreLib.Models;

public class Group : EntityBase, IName
{
    [Member] public virtual string Name { get; set; } = "";

    public Guid GroupStateId { get; set; }
    public GroupState GroupState { get; set; } = GroupState.Create(null, null);

    public ObservableList<Member> JoinedMembers { get; } = [];
    public ObservableList<Playlog> Playlogs { get; } = [];
    public ObservableList<Setlist> Setlists { get; } = [];
    public ObservableList<Songbook> Songbooks { get; } = [];
    public ObservableList<Membership> Memberships { get; } = [];

    [NotMapped] public ObservableList<Member> ConnectedMembers { get; } = [];

    //public override int CalculateChecksum()
    //{
    //    return HashCode.Combine(base.CalculateChecksum() , CurrentPlay?.Id.GetHashCode() ?? 0 , CurrentSetlist?.Id.GetHashCode() ?? 0) % int.MaxValue;
    //}

    public bool IsJustMe => Memberships.Count == 0;

    public Membership AddMembership(Member member, Role role = Role.Member)
    {
        var membership = Membership.Create(member, this, role);
        var existing = Memberships.FirstOrDefault(o => member.Equals(o.Member));
        if (existing is not null)
        {
            return existing;
        }

        Memberships.Add(membership);
        return membership;
    }

    public bool Join(Member member)
    {
        if (!Memberships.Any(o => member.Equals(o.Member)))
        {
            return false;
        }

        if (JoinedMembers.Contains(member))
        {
            return true;
        }

        JoinedMembers.Add(member);
        return true;
    }

    public bool Leave(Member member)
    {
        return JoinedMembers.Remove(member);
    }

    public static Group Create(string name, params Member[] members)
    {
        var group = new Group
        {
            Name = name,
            GroupState = GroupState.Create(null, null)
        };

        group.Songbooks.Add(Songbook.Create("Default", group));
        group.Setlists.Add(Setlist.Create("Setlist 1", group));
        foreach (var member in members)
        {
            group.AddMembership(member);
        }

        return group;
    }
}

public class GroupState : EntityBase
{
    [Member] public Guid? CurrentPlayId { get; set; }
    [Member] public Guid? CurrentSetlistItemId { get; set; }
    [Member] public Guid? CurrentSetlistId { get; set; }

    public Play? CurrentPlay { get; set; }
    public SetlistItem? CurrentSetlistItem { get; set; }
    public Setlist? CurrentSetlist { get; set; }


    public static GroupState Create(Play? play, Setlist? setlist)
    {
        return new GroupState { CurrentPlay = play, CurrentSetlist = setlist };
    }
}