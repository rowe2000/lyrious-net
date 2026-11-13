using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// What the setlist and lyrics pages need from the outside world. Each host supplies an
	/// implementation (e.g. one that calls the Lyrious API) through <see cref="App.SetlistService"/>.
	/// Methods throw when the server can't be reached; the pages report that.
	/// </summary>
	public interface ISetlistService
	{
		/// <summary>The logged-in member's group (their joined group, else their first group), or null if they have none.</summary>
		Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default);

		/// <summary>The groups the logged-in member belongs to, by name.</summary>
		Task<IReadOnlyList<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default);

		/// <summary>The members of each of the logged-in member's groups (invited ones too), keyed by group id.</summary>
		Task<IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>>> GetGroupMembersAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Starts a new group with the member as its admin, a "Default" songbook and a first setlist,
		/// like the console's CreateGroup. It doesn't become the current group; see <see cref="SelectGroupAsync"/>.
		/// </summary>
		Task<GroupSummary> CreateGroupAsync(string name, CancellationToken cancellationToken = default);

		/// <summary>Makes the group the member's current one (they join it), also for later logins.</summary>
		Task SelectGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

		Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>The setlist, or null if it doesn't exist (any more).</summary>
		Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default);

		Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default);

		/// <summary>Deletes the setlist; its songs stay in the songbook.</summary>
		Task DeleteSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>The setlist's songs in order.</summary>
		Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>One setlist entry with its song, or null if it doesn't exist (any more).</summary>
		Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default);

		/// <summary>The songs in the group's songbooks, by name.</summary>
		Task<IReadOnlyList<SongDetails>> GetSongbookSongsAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>Searches an online lyrics database by title and/or artist.</summary>
		Task<IReadOnlyList<InternetSong>> SearchInternetAsync(string query, CancellationToken cancellationToken = default);

		/// <summary>Adds a song to the group's songbook (its first one) and returns it.</summary>
		Task<SongDetails> CreateSongAsync(Guid groupId, string name, string lyrics, TimeSpan duration, CancellationToken cancellationToken = default);

		/// <summary>Deletes the song from the songbook (and so from every setlist).</summary>
		Task DeleteSongAsync(Guid songId, CancellationToken cancellationToken = default);

		/// <summary>Stores the song's name, key, tempo, beat and lyrics, and returns the updated song.</summary>
		Task<SongDetails> UpdateSongAsync(SongDetails song, CancellationToken cancellationToken = default);

		/// <summary>Replaces the song's lyrics and returns the updated song.</summary>
		Task<SongDetails> UpdateLyricsAsync(Guid songId, string lyrics, CancellationToken cancellationToken = default);

		/// <summary>Appends the songs to the end of the setlist, in the given order.</summary>
		Task AddSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default);

		/// <summary>Makes the setlist the group's current one (and clears the current song).</summary>
		Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>Makes the entry the group's current song (and its setlist the current setlist).</summary>
		Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default);
	}

	public sealed record GroupSummary(Guid Id, string Name, Guid? CurrentSetlistId, Guid? CurrentSetlistItemId);

	public enum GroupRole { Member, Conductor, Admin }

	/// <summary>A member of a group: their role, and whether they have accepted the invitation yet.</summary>
	public sealed record GroupMember(Guid MemberId, string Name, GroupRole Role, bool Approved);

	public sealed record SetlistSummary(Guid Id, string Name, Guid GroupId);

	public sealed record SongDetails(Guid Id, string Name, string Key, float Tempo, string Beat, string Lyrics);

	/// <summary>A song found online: title, artist, album, length and plain lyrics (empty if the database has none).</summary>
	public sealed record InternetSong(string Title, string Artist, string Album, TimeSpan Duration, string Lyrics, bool Instrumental);

	/// <summary>A song at a position in a setlist.</summary>
	public sealed record SetlistEntry(Guid Id, Guid SetlistId, int Position, SongDetails Song);

	/// <summary>Used until a host provides a real service, so the pages say why nothing loads.</summary>
	public sealed class UnconfiguredSetlistService : ISetlistService
	{
		private static Exception NotConnected() => new InvalidOperationException("This app isn't connected to a Lyrious server yet.");

		public Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>>> GetGroupMembersAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<GroupSummary> CreateGroupAsync(string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task DeleteSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SongDetails>> GetSongbookSongsAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<InternetSong>> SearchInternetAsync(string query, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SongDetails> CreateSongAsync(Guid groupId, string name, string lyrics, TimeSpan duration, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task DeleteSongAsync(Guid songId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SongDetails> UpdateSongAsync(SongDetails song, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SongDetails> UpdateLyricsAsync(Guid songId, string lyrics, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task AddSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default) => throw NotConnected();
	}
}
