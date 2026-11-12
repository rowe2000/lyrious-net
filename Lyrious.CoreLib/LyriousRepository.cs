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

		if (group == null)
		{
			return null;
		}

		// The creator's membership must exist before anything else in the group,
		// since the API only accepts writes from members of the owning group.
		var membership = group.CreateMembership(Me, RoleEnum.Admin);
		membership.MembershipStatusEnum = MembershipStatusEnum.Approved;
		Update(membership);

		var songbook = group.CreateSongbook("Default");
		songbook = Update(songbook);

		// Now update group with reference to setlist
		var setlist = group.CreateSetlist("Setlist 1");
		setlist = Update(setlist);

		group.CurrentSetlistId = setlist?.Id;
		group.CurrentSetlist = setlist;

		group = Update(group) ?? group;

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
		var group = GetJoinedGroup();
		if (group == null)
		{
			return null;
		}

		var membership = GetMembership(group.Id, member.Id);
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
		return await InviteMemberByQueryAsync(username, roleEnum);
	}

	public async Task<Membership?> InviteMemberByEmailAsync(string email, RoleEnum roleEnum = RoleEnum.Member)
	{
		return await InviteMemberByQueryAsync(email, roleEnum);
	}

	public async Task<Membership?> InviteMemberByPhoneAsync(string phoneNumber, RoleEnum roleEnum = RoleEnum.Member)
	{
		return await InviteMemberByQueryAsync(phoneNumber, roleEnum);
	}

	private async Task<Membership?> InviteMemberByQueryAsync(string query, RoleEnum roleEnum)
	{
		var member = await FindMemberAsync(query);
		return member == null ? null : await InviteMemberAsync(member, roleEnum);
	}

	public async Task<Membership?> ApproveMembershipAsync(Group group)
	{
		if (Me == null)
		{
			return null;
		}

		var membership = GetMembership(group.Id, Me.Id);
		if (membership == null)
		{
			return null;
		}

		membership.MembershipStatusEnum = MembershipStatusEnum.Approved;
		membership = Update(membership);

		if (membership != null && Me.JoinedGroupId == null)
		{
			await JoinGroupAsync(group);
		}

		return membership;
	}

	public async Task<Setlist> CreateSetListAsync(string setlistName, Group? group = null)
	{
		group ??= GetJoinedGroup() ?? throw new InvalidOperationException("No joined group.");

		var setlist = group.CreateSetlist(setlistName);
		return Update(setlist) ?? throw new Exception();
	}

	public async Task<bool> JoinGroupAsync(Group group)
	{
		if (Me == null)
		{
			return false;
		}

		// Me usually comes from the login response, which carries no memberships; load the one for this group.
		var membership = GetMembership(group.Id, Me.Id);
		if (membership != null && !Me.Memberships.Contains(membership))
		{
			Me.Memberships.Add(membership);
		}

		if (Me.Join(group))
		{
			return Update(Me) is not null;
		}

		return false;
	}

	public async Task<Play?> PlayAsync(SetlistItem? setlistItem, Song? song = null, string? key = null, float? tempo = null)
	{
		song ??= setlistItem?.Song;
		if (song == null || Me == null)
		{
			return null;
		}

		var group = GetJoinedGroup();
		if (group == null)
		{
			return null;
		}

		var isConductor = GetMembership(group.Id, Me.Id)?.RoleEnum == RoleEnum.Conductor;
		if (!isConductor)
		{
			return null;
		}

		var today = DateTime.Now.Date;
		var playlog = Get<Playlog>(o => o.GroupId == group.Id && o.CreatedAt.Date == today)
			.OrderBy(o => o.CreatedAt)
			.LastOrDefault();
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

	private Group? GetJoinedGroup()
	{
		var joinedGroupId = Me?.JoinedGroupId;
		return joinedGroupId == null ? null : Get<Group>(o => o.Id == joinedGroupId).FirstOrDefault();
	}

	private Membership? GetMembership(Guid groupId, Guid memberId)
	{
		return Get<Membership>(o => o.GroupId == groupId && o.MemberId == memberId && !o.Removed).FirstOrDefault();
	}

	public void SetIdentity(IIdentity? identity)
	{
		Me = identity != null ? context.Members.FirstOrDefault(o => o.UserName == identity.Name) : null;
	}

	protected override bool HasAccess<TEntity>(TEntity entity)
	{
		if (Me == null)
			return false;

		return entity switch
		{
			Member member => member.Id == Me.Id || SharesGroupWith(member.Id),
			Role => true,
			Group group => group.Id == Me.JoinedGroupId || IsMemberOf(group.Id),
			_ => IsMemberOf(GroupIdOf(entity))
		};
	}

	/// <summary>
	/// Whether the current member may create or update <paramref name="entity"/>.
	/// For an existing entity both the stored and the incoming version must be writable,
	/// so an entity can't be moved into or out of a group the member doesn't belong to.
	/// </summary>
	public bool CanWrite<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		if (Me == null)
			return false;

		var existing = context.Find<TEntity>(entity.Id);
		if (existing == null)
			return CanCreate(entity);

		return CanModify(existing) && CanModify(entity);
	}

	/// <summary>
	/// Whether the current member may remove the stored entity with <paramref name="id"/>.
	/// Unknown ids are treated as not removable.
	/// </summary>
	public bool CanRemove<TEntity>(Guid id) where TEntity : class, IEntity, new()
	{
		if (Me == null)
			return false;

		var existing = context.Find<TEntity>(id);
		return existing != null && CanModify(existing);
	}

	private bool CanCreate(IEntity entity)
	{
		return entity switch
		{
			// Anyone signed in may start a new group.
			Group => true,
			// The first membership of a freshly created group bootstraps its creator.
			Membership m when m.MemberId == Me!.Id
			                  && context.Groups.Any(g => g.Id == m.GroupId)
			                  && !context.Memberships.Any(o => o.GroupId == m.GroupId && !o.Removed) => true,
			_ => CanModify(entity)
		};
	}

	private bool CanModify(IEntity entity)
	{
		return entity switch
		{
			Member member => member.Id == Me!.Id && (member.JoinedGroupId == null || IsMemberOf(member.JoinedGroupId)),
			Role => false,
			SetlistItem item => IsMemberOf(GroupIdOf(item)) && IsMemberOf(SongGroupId(item.SongId)),
			Play play => IsMemberOf(GroupIdOf(play)) && IsMemberOf(SongGroupId(play.SongId)),
			_ => IsMemberOf(GroupIdOf(entity))
		};
	}

	/// <summary>
	/// Resolves the owning group through foreign keys, so it also works for entities whose
	/// navigation properties aren't loaded (e.g. deserialized request bodies).
	/// </summary>
	private Guid? GroupIdOf(IEntity entity)
	{
		return entity switch
		{
			Group group => group.Id,
			Membership membership => membership.GroupId,
			Playlog playlog => playlog.GroupId,
			Setlist setlist => setlist.GroupId,
			Songbook songbook => songbook.GroupId,
			Song song => context.Songbooks.Where(o => o.Id == song.SongbookId).Select(o => o.GroupId).FirstOrDefault(),
			SetlistItem item => context.Setlists.Where(o => o.Id == item.SetlistId).Select(o => (Guid?)o.GroupId).FirstOrDefault(),
			Play play => context.Playlogs.Where(o => o.Id == play.PlaylogId).Select(o => (Guid?)o.GroupId).FirstOrDefault(),
			_ => null
		};
	}

	private Guid? SongGroupId(Guid songId)
	{
		return context.Songs
			.Where(o => o.Id == songId)
			.Join(context.Songbooks, s => s.SongbookId, b => b.Id, (s, b) => b.GroupId)
			.FirstOrDefault();
	}

	/// <summary>Whether the current member has a (non-removed) membership in the group.</summary>
	public bool IsMember(Guid groupId) => IsMemberOf(groupId);

	/// <summary>Ids of the groups the current member has a (non-removed) membership in.</summary>
	public Guid[] GetMyGroupIds()
	{
		if (Me == null)
			return [];

		var meId = Me.Id;
		return context.Memberships.Where(m => m.MemberId == meId && !m.Removed).Select(m => m.GroupId).Distinct().ToArray();
	}

	/// <summary>
	/// The groups whose members may see <paramref name="entity"/>: its owning group, or for a member
	/// every group they belong to. Used to route change notifications.
	/// </summary>
	public Guid[] GroupIdsOf(IEntity entity)
	{
		if (entity is Member member)
		{
			return context.Memberships.Where(m => m.MemberId == member.Id && !m.Removed).Select(m => m.GroupId).Distinct().ToArray();
		}

		return GroupIdOf(entity) is { } groupId ? [groupId] : [];
	}

	private bool IsMemberOf(Guid? groupId)
	{
		if (Me == null || groupId == null)
			return false;

		var meId = Me.Id;
		return context.Memberships.Any(m => m.GroupId == groupId && m.MemberId == meId && !m.Removed);
	}

	private bool SharesGroupWith(Guid memberId)
	{
		if (Me == null)
			return false;

		var meId = Me.Id;
		var myGroupIds = context.Memberships.Where(m => m.MemberId == meId && !m.Removed).Select(m => m.GroupId);
		return context.Memberships.Any(m => m.MemberId == memberId && !m.Removed && myGroupIds.Contains(m.GroupId));
	}
}
