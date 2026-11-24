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
		// No setlist to start with: the band makes its own.

		await JoinGroupAsync(group);

		return group;
	}

	/// <summary>
	/// Makes sure the current member has their private band (named <see cref="Group.PrivateName"/>,
	/// with only them in it, a songbook and a first setlist), so they can keep songs and setlists
	/// without ever joining a band; a member who hasn't joined a band joins it. Made here on the
	/// server, once per member, so devices that are offline can't each make one.
	/// </summary>
	/// <returns>The private band.</returns>
	public Group? EnsurePrivateGroup()
	{
		if (Me == null)
			return null;

		var meId = Me.Id;
		var myGroupIds = context.Memberships.Where(m => m.MemberId == meId && !m.Removed).Select(m => m.GroupId);
		var group = context.Groups.FirstOrDefault(g => g.IsPrivate && !g.Removed && myGroupIds.Contains(g.Id));
		if (group == null)
		{
			group = Update(new Group { Name = Group.PrivateName, IsPrivate = true })!;
			Update(new Membership { GroupId = group.Id, MemberId = meId, RoleEnum = RoleEnum.Admin, MembershipStatusEnum = MembershipStatusEnum.Approved });
		}

		// Each part is saved by itself, so a band whose making stopped half-way (e.g. a failed
		// save) gets the rest here, the next time, instead of staying without a songbook for good.
		var groupId = group.Id;
		if (!context.Songbooks.Any(b => b.GroupId == groupId && !b.Removed))
		{
			Update(new Songbook { Name = "Default", GroupId = groupId });
		}

		if (Me.JoinedGroupId == null)
		{
			Me.JoinedGroupId = group.Id;
			context.SaveChanges();
		}

		return group;
	}

	/// <summary>Whether the group is someone's private band (which only the server makes and changes).</summary>
	private bool IsPrivateGroup(Guid? groupId) => groupId != null && context.Groups.Any(g => g.Id == groupId && g.IsPrivate);

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

	/// <summary>
	/// The site managers: usernames or email addresses (any case), set at startup from the server's
	/// SITE_MANAGERS setting (e.g. a Railway variable, "robert,anna@example.com"). A site manager is
	/// an admin of every band, also ones they're not in, except other members' private bands.
	/// </summary>
	public static IReadOnlySet<string> SiteManagers { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Reads a SITE_MANAGERS value: names or addresses separated by commas, semicolons or spaces.</summary>
	public static IReadOnlySet<string> ParseSiteManagers(string? value)
		=> new HashSet<string>((value ?? "").Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

	/// <summary>The claim an elevated login's token carries (see api/auth/elevate): site-manager rights.</summary>
	public const string SiteManagerClaim = "lyrious:site-manager";

	// Whether the current identity is an elevated login (its token carries SiteManagerClaim).
	private bool elevated;

	/// <summary>
	/// Whether the current member acts as a site manager: listed (see <see cref="SiteManagers"/>) and
	/// logged in elevated (their token carries <see cref="SiteManagerClaim"/>); a listed member's normal
	/// login has a member's rights.
	/// </summary>
	public bool IsSiteManager => elevated && Me is { } me && IsSiteManagerAccount(me);

	/// <summary>Whether the member is listed as a site manager (may log in elevated): their username or email address is listed.</summary>
	public static bool IsSiteManagerAccount(Member member)
		=> SiteManagers.Count > 0
		   && ((member.UserName is { } name && SiteManagers.Contains(name)) || (member.Email is { Length: > 0 } email && SiteManagers.Contains(email)));

	/// <summary>A band a site manager may manage without being in it: any band but a private one.</summary>
	private bool IsManagedBySiteManager(Guid? groupId)
		=> IsSiteManager && groupId != null && context.Groups.Any(g => g.Id == groupId && !g.IsPrivate);

	public void SetIdentity(IIdentity? identity)
	{
		elevated = identity is System.Security.Claims.ClaimsIdentity claims && claims.HasClaim(SiteManagerClaim, "true");
		var member = identity != null ? context.Members.FirstOrDefault(o => o.UserName == identity.Name) : null;
		// A disabled member is no one here, also with a token from before they were disabled.
		Me = member != null && IsDisabled(member) ? null : member;
	}

	/// <summary>Whether a site manager has disabled the member: they can't log in or use their account until enabled again.</summary>
	public static bool IsDisabled(Member member) => member.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

	/// <summary>The members a site manager has disabled (see <see cref="IsDisabled"/>), for a site manager; none for anyone else.</summary>
	public IReadOnlyList<Guid> DisabledMembersForSiteManager()
	{
		if (!IsSiteManager)
			return [];

		var now = DateTimeOffset.UtcNow;
		// Compared here, not in the database: SQLite can't compare DateTimeOffset values.
		return context.Members.Where(m => !m.Removed && m.LockoutEnd != null).AsEnumerable()
			.Where(m => m.LockoutEnd > now)
			.Select(m => m.Id)
			.ToList();
	}

	/// <summary>
	/// Disables a member's account (they can't log in or use it) or enables it again, as a site
	/// manager. Not the site manager's own account. False when that isn't allowed or there's no such member.
	/// </summary>
	public bool SetMemberDisabled(Guid memberId, bool disabled)
	{
		if (MemberForSiteManager(memberId) is not { } member || member.Id == Me?.Id)
			return false;

		// Locked out for good, through Identity's lockout (which the login checks too).
		member.LockoutEnabled = true;
		member.LockoutEnd = disabled ? DateTimeOffset.MaxValue : null;
		context.SaveChanges();
		return true;
	}

	protected override bool HasAccess<TEntity>(TEntity entity)
	{
		if (Me == null)
			return false;

		return entity switch
		{
			Member member => member.Id == Me.Id || SharesGroupWith(member.Id),
			Role => true,
			// Not part of a band: every member reads the published ones, site managers all of them.
			ReleaseNote note => note.Published || IsSiteManager,
			// Only bands they're in (a member removed from their joined band no longer sees it).
			Group group => IsMemberOf(group.Id),
			// A member's own song settings: theirs, and seen by the admins of the song's band too.
			SongSetting setting => CanSeeSongSetting(setting),
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

		// A band's MIDI settings are stored only in their form (see Group.MidiExpressions and Group.MidiCommands).
		// A member's song settings: a transpose within an octave, lyrics no longer than a song's.
		if (entity is SongSetting songSetting && (songSetting.Transpose is < SongSetting.MinTranspose or > SongSetting.MaxTranspose || (songSetting.Lyrics?.Length ?? 0) > SongSetting.MaxLyricsLength))
			return false;

		if (entity is Group midiBand && (!Group.IsValidMidiExpressions(midiBand.MidiExpressions) || !Group.IsValidMidiCommands(midiBand.MidiCommands)
		                                 || (midiBand.LyricsHeader?.Length ?? 0) > Group.MaxLyricsHeaderLength))
			return false;

		var existing = context.Find<TEntity>(entity.Id);
		if (existing == null)
			return CanCreate(entity);

		return existing switch
		{
			Membership stored when entity is Membership changed => CanChangeMembership(stored, changed),
			// Their own: the joined group they move to must be one of theirs (the one they leave may
			// not be any more, e.g. after being removed from it). Someone else's: their name, by an
			// admin of a band they're in.
			Member stored when entity is Member changed => stored.Id == Me.Id ? CanModify(changed)
				: IsSiteManager ? CanSiteManagerChangeMember(stored, changed)
				: CanRenameBandMember(stored, changed),
			// Whether a band is private never changes; a private band keeps its name and isn't deleted.
			Group stored when entity is Group changed && (changed.IsPrivate != stored.IsPrivate
				|| stored.IsPrivate && (changed.Name != stored.Name || changed.Removed)) => false,
			// Any member may change the band (its name, current setlist and song); only an admin may delete it.
			Group stored when entity is Group changed && changed.Removed && !stored.Removed => IsAdminOf(stored.Id),
			// Only an admin changes the band's MIDI expressions and commands, and its lyrics header (every member uses them).
			Group stored when entity is Group changed && (changed.MidiExpressions != stored.MidiExpressions || changed.MidiCommands != stored.MidiCommands
			                                              || changed.LyricsHeader != stored.LyricsHeader || changed.NextSongOnStopAtEnd != stored.NextSongOnStopAtEnd)
				=> IsAdminOf(stored.Id) && CanModify(existing) && CanModify(entity),
			_ => CanModify(existing) && CanModify(entity),
		};
	}

	/// <summary>
	/// An admin may change the name of the members of the bands they manage, and nothing else of
	/// them: which band they've joined stays theirs to choose. (The login, email and password aren't
	/// part of what's stored this way at all.)
	/// </summary>
	private bool CanRenameBandMember(Member stored, Member changed)
	{
		// An admin changes the name only: not where they are, nor their own settings.
		if (string.IsNullOrWhiteSpace(changed.Name) || changed.JoinedGroupId != stored.JoinedGroupId || changed.Removed != stored.Removed
		    || changed.Settings != stored.Settings)
			return false;

		var meId = Me!.Id;
		var managed = context.Memberships.Where(m => m.MemberId == meId && !m.Removed && m.RoleEnum == RoleEnum.Admin).Select(m => m.GroupId);
		return context.Memberships.Any(m => m.MemberId == stored.Id && !m.Removed && managed.Contains(m.GroupId));
	}

	/// <summary>
	/// Members are managed by the band's admins: they invite, change roles and remove. A member may
	/// accept their own invitation and leave, but not change their own role. The last admin can't be
	/// demoted or removed (the band would have no one to manage it).
	/// </summary>
	private bool CanChangeMembership(Membership stored, Membership changed)
	{
		if (changed.GroupId != stored.GroupId || changed.MemberId != stored.MemberId || !IsMemberOf(stored.GroupId))
			return false;

		// A private band's only member stays as they are (no leaving, no other role).
		if (IsPrivateGroup(stored.GroupId))
			return false;

		var loses = stored.RoleEnum == RoleEnum.Admin && !stored.Removed && (changed.Removed || changed.RoleEnum != RoleEnum.Admin);
		if (loses && !HasOtherAdmin(stored))
			return false;

		if (IsAdminOf(stored.GroupId))
			return true;

		// Their own: accepting (status) or leaving, with the role as it was.
		return stored.MemberId == Me!.Id && changed.RoleEnum == stored.RoleEnum;
	}

	private bool HasOtherAdmin(Membership membership)
		=> context.Memberships.Any(o => o.GroupId == membership.GroupId && o.Id != membership.Id && !o.Removed && o.RoleEnum == RoleEnum.Admin);

	/// <summary>Whether the current member is an admin of the group.</summary>
	public bool IsAdminOf(Guid? groupId)
	{
		if (Me == null || groupId == null)
			return false;

		if (IsManagedBySiteManager(groupId))
			return true;

		var meId = Me.Id;
		return context.Memberships.Any(m => m.GroupId == groupId && m.MemberId == meId && !m.Removed && m.RoleEnum == RoleEnum.Admin);
	}

	/// <summary>The members of the current member's groups (themselves included), invited ones too.</summary>
	public Guid[] GetMyBandmateIds()
	{
		var groupIds = GetMyGroupIds();
		return context.Memberships.Where(m => groupIds.Contains(m.GroupId) && !m.Removed).Select(m => m.MemberId).Distinct().ToArray();
	}

	/// <summary>Notes that the current member has just logged in (or opened the app connected).</summary>
	/// <summary>Stores the current member's own settings (a JSON object, or null for none).</summary>
	/// <summary>
	/// What a site manager may change of another member: their name (not empty). Where they are, their
	/// removal and their settings stay (their settings through <see cref="SaveSettingsOf"/>).
	/// </summary>
	private static bool CanSiteManagerChangeMember(Member stored, Member changed)
		=> !string.IsNullOrWhiteSpace(changed.Name) && changed.JoinedGroupId == stored.JoinedGroupId
		   && changed.Removed == stored.Removed && changed.Settings == stored.Settings;

	/// <summary>A member's own settings (a JSON object, or null), for a site manager; null if they aren't one or there's no such member.</summary>
	public Member? MemberForSiteManager(Guid memberId)
		=> IsSiteManager ? context.Members.FirstOrDefault(m => m.Id == memberId && !m.Removed) : null;

	/// <summary>Stores another member's own settings (a JSON object, or null), as a site manager.</summary>
	public bool SaveSettingsOf(Guid memberId, string? settings)
	{
		if (MemberForSiteManager(memberId) is not { } member)
			return false;

		member.Settings = settings;
		context.SaveChanges();
		return true;
	}

	public void SaveMySettings(string? settings)
	{
		if (Me == null)
			return;

		Me.Settings = settings;
		context.SaveChanges();
	}

	public void RecordLogin()
	{
		if (Me == null)
			return;

		Me.LastLoginAt = DateTime.Now;
		context.SaveChanges();
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
		return existing switch
		{
			null => false,
			// Removing a member: as for marking the membership removed.
			Membership membership => CanChangeMembership(membership, new Membership { Id = membership.Id, GroupId = membership.GroupId, MemberId = membership.MemberId, RoleEnum = membership.RoleEnum, Removed = true }),
			// Deleting a band: its admins only; a private band isn't deleted.
			Group group => !group.IsPrivate && IsAdminOf(group.Id),
			_ => CanModify(existing),
		};
	}

	private bool CanCreate(IEntity entity)
	{
		return entity switch
		{
			// Anyone signed in may start a new group; private bands are made by the server only.
			Group group => !group.IsPrivate,
			// No one else can be added to a private band.
			Membership m when IsPrivateGroup(m.GroupId) => false,
			// The first membership of a freshly created group bootstraps its creator.
			Membership m when m.MemberId == Me!.Id
			                  && context.Groups.Any(g => g.Id == m.GroupId)
			                  && !context.Memberships.Any(o => o.GroupId == m.GroupId && !o.Removed) => true,
			// Inviting: the band's admins, and a member can't be in a band twice.
			Membership m => IsAdminOf(m.GroupId)
			                && !context.Memberships.Any(o => o.GroupId == m.GroupId && o.MemberId == m.MemberId && !o.Removed),
			_ => CanModify(entity)
		};
	}

	private bool CanModify(IEntity entity)
	{
		return entity switch
		{
			Member member => member.Id == Me!.Id && (member.JoinedGroupId == null || IsMemberOf(member.JoinedGroupId)),
			Role => false,
			// Only site managers add, change or remove release notes.
			ReleaseNote => IsSiteManager,
			SetlistItem item => IsMemberOf(GroupIdOf(item)) && IsMemberOf(SongGroupId(item.SongId)),
			Play play => IsMemberOf(GroupIdOf(play)) && IsMemberOf(SongGroupId(play.SongId)),
			SongSetting setting => CanSeeSongSetting(setting) && IsInGroup(setting.MemberId, SongGroupId(setting.SongId)),
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
			SongSetting setting => SongGroupId(setting.SongId),
			_ => null
		};
	}

	/// <summary>A member's song settings: their own, or any member's of a band the caller is an admin of.</summary>
	private bool CanSeeSongSetting(SongSetting setting)
	{
		var groupId = SongGroupId(setting.SongId);
		return IsMemberOf(groupId) && (setting.MemberId == Me!.Id || IsAdminOf(groupId));
	}

	private bool IsInGroup(Guid memberId, Guid? groupId)
		=> groupId != null && context.Memberships.Any(m => m.GroupId == groupId && m.MemberId == memberId && !m.Removed);

	/// <summary>Who gets a member's song settings when they change: that member, and the admins of the song's band.</summary>
	public Guid[] SongSettingAudience(SongSetting setting)
	{
		var groupId = SongGroupId(setting.SongId);
		var admins = groupId == null
			? []
			: context.Memberships.Where(m => m.GroupId == groupId && !m.Removed && m.RoleEnum == RoleEnum.Admin).Select(m => m.MemberId).ToArray();
		return admins.Append(setting.MemberId).Distinct().ToArray();
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
		var mine = context.Memberships.Where(m => m.MemberId == meId && !m.Removed).Select(m => m.GroupId);
		// A site manager also follows every band but other members' private ones.
		return IsSiteManager
			? context.Groups.Where(g => !g.Removed && (!g.IsPrivate || mine.Contains(g.Id))).Select(g => g.Id).ToArray()
			: mine.Distinct().ToArray();
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

		if (IsManagedBySiteManager(groupId))
			return true;

		var meId = Me.Id;
		return context.Memberships.Any(m => m.GroupId == groupId && m.MemberId == meId && !m.Removed);
	}

	private bool SharesGroupWith(Guid memberId)
	{
		if (Me == null)
			return false;

		// A site manager sees every member (as they see every band).
		if (IsSiteManager)
			return true;

		var meId = Me.Id;
		var myGroupIds = context.Memberships.Where(m => m.MemberId == meId && !m.Removed).Select(m => m.GroupId);
		return context.Memberships.Any(m => m.MemberId == memberId && !m.Removed && myGroupIds.Contains(m.GroupId));
	}
}
