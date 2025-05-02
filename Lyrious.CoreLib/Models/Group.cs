using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib.Models;

public sealed class Group : Entity, IName
{
	[MaxLength(100)]
	[DataMember]
	public string Name { get; set; } = "";
	//[Member]
	//public Guid? GroupStateId { get; set; }

	//[JsonIgnore]
	//public GroupState? GroupState { get; set; }

	[DataMember] public Guid? CurrentSetlistId { get; set; }
	[DataMember] public Guid? CurrentPlayId { get; set; }
	[DataMember] public Guid? CurrentSetlistItemId { get; set; }

	[JsonIgnore] public Setlist? CurrentSetlist { get; set; } = null;
	[JsonIgnore] public SetlistItem? CurrentSetlistItem { get; set; } = null;
	[JsonIgnore] public Play? CurrentPlay { get; set; } = null;


	[JsonIgnore] public ObservableList<Member> JoinedMembers { get; set; } = [];
	[JsonIgnore] public ObservableList<Playlog> Playlogs { get; set; } = [];
	[JsonIgnore] public ObservableList<Setlist> Setlists { get; set; } = [];
	[JsonIgnore] public ObservableList<Songbook> Songbooks { get; set; } = [];
	[JsonIgnore] public ObservableList<Membership> Memberships { get; set; } = [];

	[NotMapped]
	[JsonIgnore] public ObservableList<Member> ConnectedMembers { get; set; } = [];

	//[NotMapped]
	//[JsonIgnore]
	//public GroupState? GroupState { get; set; }

	//public override int CalculateChecksum()
	//{
	//    return HashCode.Combine(base.CalculateChecksum(), CurrentPlay?.Id.GetHashCode() ?? 0, CurrentSetlist?.Id.GetHashCode() ?? 0) % int.MaxValue;
	//}

	public bool IsJustMe => Memberships.Count == 0;

	public Membership CreateMembership(Member member, RoleEnum role = RoleEnum.Member)
	{
		var existing = Memberships.FirstOrDefault(o => member.UserName?.Equals(o.Member?.UserName) == true);
		if (existing is not null)
		{
			if (!member.Memberships.Contains(existing))
			{
				member.Memberships.Add(existing);
			}

			return existing;
		}

		var membership = Membership.Create(member, this, role);

		member.Memberships.Add(membership);

		return membership;
	}

	public bool Join(Member member)
	{
		if (!Memberships.Any(o => member.Equals(o.Member)))
		{
			return false;
		}

		member.JoinedGroup?.Leave(member);

		member.JoinedGroup = this;
		member.JoinedGroupId = Id;

		if (!JoinedMembers.Contains(member))
		{
			JoinedMembers.Add(member);
		}

		return true;
	}

	public bool Leave(Member member)
	{
		member.JoinedGroup = null;
		member.JoinedGroupId = null;
		return JoinedMembers.Remove(member);
	}

	public Setlist CreateSetlist(string setlistName)
	{
		var setlist = Setlist.Create(this, setlistName);
		if (Setlists.Count == 0)
		{
			CurrentSetlist = setlist;
			CurrentSetlistId = setlist.Id;
		}

		Setlists.Add(setlist);
		return setlist;
	}

	public Songbook CreateSongbook(string songbookName)
	{
		var songbook = Songbook.Create(this, songbookName);
		Songbooks.Add(songbook);
		return songbook;
	}

	public static Group Create(string name)
	{
		return new Group { Name = name, };
	}

	public Playlog CreatePlaylog()
	{
		var playlog = Playlog.Create(this);
		Playlogs.Add(playlog);
		return playlog;
	}
}