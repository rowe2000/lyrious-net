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

		// Save the songbook too

		await JoinGroup(group);

		return group;
	}

	private async Task JoinGroup(Group group)
	{
		group.Join(Me);
		Update(Me);
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

	public async Task<Membership?> AddMemberAsync(Group group, Member member, RoleEnum roleEnum = RoleEnum.Member)
	{
		var membership = group.CreateMembership(member, roleEnum);
		return Update(membership);
	}

	public async Task<Setlist> CreateSetListAsync(string setlistName, Group group)
	{
		var setlist = group.CreateSetlist(setlistName);
		return Update(setlist) ?? throw new Exception();
	}

	public async Task<bool> JoinGroupAsync(Group group, Member member)
	{
		if (group.Join(member))
		{
			return Update(group) is not null;
		}

		return false;
	}

	public async Task<Play?> PlayAsync(Group group, SetlistItem? setlistItem, Song? song, Member conductor, string? key = null, float? tempo = null)
	{
		song ??= setlistItem?.Song;
		if (song == null)
		{
			return null;
		}

		var playlog = group.Playlogs.LastOrDefault(o => o.CreatedAt.Date == DateTime.Now.Date);
		if (playlog == null)
		{
			playlog = group.CreatePlaylog();
			playlog = Update(playlog);
		}

		var play = playlog?.CreatePlay(song, conductor, key ?? song.Key, tempo ?? song.Tempo);
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
}