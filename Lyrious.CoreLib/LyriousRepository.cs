using System.Security.Principal;
using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib;

public class LyriousRepository(LyriousContext context)
	: Repository<LyriousContext, Member, Role>(context)
{
	public IEnumerable<Group> CreateGroupsAsync(IEnumerable<string> groupNames)
	{
		return Update(groupNames.Select(Group.Create));
	}

	public async Task<Group?> CreateGroupAsync(string groupName)
	{
		if (Me == null)
		{
			return null;
		}
		
		// Create group without CurrentSetlist
		var group = Group.Create(groupName);
		group = Update(group);

		var songbook = group.CreateSongbook("Default");
		songbook = Update(songbook);

		// Now update group with reference to setlist
		var setlist = group.CreateSetlist("Setlist 1");
		setlist = Update(setlist);

		group.CurrentSetlistId = setlist.Id;
		group.CurrentSetlist = setlist;

		group = Update(group);

		if (group == null)
		{
			return null;
 		}

		var membership = group.CreateMembership(Me, RoleEnum.Admin);
		membership.MembershipStatusEnum = MembershipStatusEnum.Approved;
		Update(membership);

		await JoinGroupAsync(group);

		return group;
	}

	public async Task<Song?> CreateSongAsync(string songName, string key, float tempo = 120, TimeSpan? length = null)
	{
		return await CreateSongAsync(null, songName, key, tempo, length);
	}

	public async Task<Song?> CreateSongAsync(Songbook? songbook, string songName, string key, float tempo = 120, TimeSpan? length = null)
	{
		songbook ??= Me?.JoinedGroup?.Songbooks.FirstOrDefault();
		if (songbook is null)
		{
			return null;
		}

		var song = songbook.CreateSong(songName, key, tempo, length);
		return Update(song);
	}

	public async Task<IEnumerable<SetlistItem>> AddSongsAsync(Setlist setlist, IEnumerable<Song> songs)
	{
		var setlistItems = setlist.AddSongs(songs);
		return Update(setlistItems);
	}

	public async Task<Membership?> InviteMemberAsync(Member member, RoleEnum roleEnum = RoleEnum.Member)
	{
		var group = Me?.JoinedGroup;
		if (group == null)
		{
			return null;
		}

		var membership = Get<Membership>(o => o.MemberId == member.Id).FirstOrDefault();
		if (membership != null)
		{
			if (membership.RoleEnum == roleEnum)
			{
				return membership;
			}

			membership.RoleEnum = roleEnum;
			return Update(membership);

		}

		membership = group.CreateMembership(member, roleEnum);
		return Update(membership);
	}

	public async Task<Membership?> InviteMemberByUsernameAsync(string username, RoleEnum roleEnum = RoleEnum.Member)
	{
		var member = Get<Member>(o => o.UserName == username).FirstOrDefault();
		if (member == null)
		{
			return null;
		}

		return await InviteMemberAsync(member);
	}

	public async Task<Membership?> InviteMemberByEmailAsync(string email, RoleEnum roleEnum = RoleEnum.Member)
	{
		var member = Get<Member>(o => o.Email == email).FirstOrDefault();
		if (member == null)
		{
			return null;
		}

		return await InviteMemberAsync(member);
	}

	public async Task<Membership?> InviteMemberByPhoneAsync(string phoneNumber, RoleEnum roleEnum = RoleEnum.Member)
	{
		var member = Get<Member>(o => o.PhoneNumber == phoneNumber).FirstOrDefault();
		if (member == null)
		{
			return null;
		}

		return await InviteMemberAsync(member);
	}

	public async Task<Membership?> ApproveMembershipAsync(Group group)
	{
		if (Me == null)
		{
			return null;
		}
		
		var membership = group.ApproveMembership(Me);
		if (membership == null)
		{
			return null;
		}

		if (Me.JoinedGroupId == null)
		{
			await JoinGroupAsync(group);
		}
		
		return Update(membership);
	}

	public async Task<Setlist> CreateSetListAsync(string setlistName, Group? group = null)
	{
		group ??= Me?.JoinedGroup;

		var setlist = group.CreateSetlist(setlistName);
		return Update(setlist) ?? throw new Exception();
	}

	public async Task<bool> JoinGroupAsync(Group group)
	{
		if (Me?.Join(group) == true)
		{
			return Update(Me) is not null;
		}

		return false;
	}

	public async Task<Play?> PlayAsync(SetlistItem? setlistItem, Song? song = null, string? key = null, float? tempo = null)
	{
		song ??= setlistItem?.Song;
		if (song == null)
		{
			return null;
		}

		var group = Me?.JoinedGroup;
		if (group == null)
		{
			return null;
		}

		var isConductor = Me?.Memberships.FirstOrDefault(o => o.GroupId == group.Id)?.RoleEnum == RoleEnum.Conductor;
		if (!isConductor)
		{
			return null;
		}
		
		var playlog = group.Playlogs.LastOrDefault(o => o.CreatedAt.Date == DateTime.Now.Date);
		if (playlog == null)
		{
			playlog = group.CreatePlaylog();
			playlog = Update(playlog);
		}

		var play = playlog?.CreatePlay(song, Me, key ?? song.Key, tempo ?? song.Tempo);
		if (play == null)
		{
			return null;
		}

		play = Update(play);

		group.CurrentPlay = play;
		group.CurrentPlayId = play?.Id;
		group.CurrentSetlistItem = setlistItem;
		group.CurrentSetlistItemId = setlistItem?.Id;

		Update(group);

		return play;
	}

	public void SetIdentity(IIdentity? identity)
	{
		Me = identity != null ? context.Members.FirstOrDefault(o => o.UserName == identity.Name) : null;
	}

	protected override bool HasAccess<TEntity>(TEntity entity)
	{
		return entity switch
		{
			Group group => HasAccess(group),
			Member member => HasAccess(member),
			Membership membership => HasAccess(membership),
			Play play => HasAccess(play.Playlog?.Group),
			Playlog playlog => HasAccess(playlog.Group),
			Role role => Me != null,
			Setlist setlist => HasAccess(setlist.Group),
			SetlistItem setlistItem => HasAccess(setlistItem.Setlist?.Group),
			Song song => HasAccess(song.Songbook?.Group),
			Songbook songbook => HasAccess(songbook.Group),
			_ => false
		};
	}

	private bool HasAccess(Membership membership)
	{
		if (Me == null)
			return false;

		if (membership == null)
			return false;

		if (membership.MemberId == Me.Id)
			return true;

		return context.Memberships.Any(m => m.Id == membership.Id && Me.Id == m.MemberId);
	}

	private bool HasAccess(Member member)
	{
		if (Me == null)
			return false;

		if (member == null)
			return false;

		if (member.Id == Me.Id)
			return true;

		var otherMemberships = context.Memberships.Where(m => member.Id == m.MemberId).ToArray()
			.Distinct().ToArray();
		var otherMembershipIds = otherMemberships.Select(o => o.GroupId);

		var meMemberships = context.Memberships.Where(m => Me.Id == m.MemberId).Distinct().ToArray();
		var meMembershipIds = meMemberships.Select(o => o.GroupId);

		var memberships = meMembershipIds.Except(otherMembershipIds).ToArray();
		return memberships.Any();
	}

	private bool HasAccess(Group? group)
	{
		if (Me == null)
			return false;

		if (group == null)
			return false;

		if (group.Id == Me.JoinedGroup?.Id)
			return true;

		return context.Memberships.Any(m => m.GroupId == group.Id && Me.Id == m.MemberId);
	}
}