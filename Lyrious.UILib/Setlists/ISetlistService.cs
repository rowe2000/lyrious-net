using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// What the setlist and lyrics pages need from the outside world. Each host supplies an
	/// implementation (e.g. one that calls the Lyrious API) through <see cref="App.SetlistService"/>.
	/// Methods throw when the server can't be reached; the pages report that.
	/// </summary>
	public interface ISetlistService
	{
		/// <summary>Whether the server can be reached, and how many changes made here it hasn't got yet.</summary>
		SyncStatus Status { get; }

		/// <summary>Raised (on any thread) when <see cref="Status"/> changes.</summary>
		event Action? StatusChanged;

		/// <summary>Sends the changes made here and fetches others' now (if the server can be reached).</summary>
		Task SyncNowAsync();

		/// <summary>
		/// Raised (on a background thread) with the group the logged-in member joined in another
		/// session (another device or window): this session should switch to it too.
		/// </summary>
		event Action<Guid>? JoinedGroupChanged;

		/// <summary>
		/// Raised (on a background thread) when another member changes one of the member's groups,
		/// e.g. its current setlist. Only while live updates are started.
		/// </summary>
		event Action<GroupSummary>? GroupChanged;

		/// <summary>
		/// Raised (on a background thread) with the band and true/false when another member of the
		/// member's joined band starts or stops playing. Only while live updates are started.
		/// </summary>
		event Action<Guid, bool>? PlaybackChanged;

		/// <summary>
		/// Raised (on a background thread) with the band and where in the song another member of the
		/// member's joined band is, just before their play, stop or back to the start: for timed lyrics
		/// the time in the song in seconds, else how far down the lyrics (0 to 1).
		/// </summary>
		event Action<Guid, double>? PlaybackPositionChanged;

		/// <summary>Raised (on any thread) when a bandmate comes online (true) or goes offline.</summary>
		event Action<Guid, bool>? MemberConnectionChanged;

		/// <summary>
		/// Raised (on a background thread) when the server sends a change to a member or a membership
		/// of the member's bands: a name, the band someone has joined, a role, someone invited or removed.
		/// </summary>
		event Action? MembersChanged;

		/// <summary>Tells the other members of the joined band that this member started or stopped playing.</summary>
		Task SendPlaybackAsync(bool playing);

		/// <summary>Tells the other members of the joined band where in the song this member is (see <see cref="PlaybackPositionChanged"/>).</summary>
		Task SendPlaybackPositionAsync(double position);

		/// <summary>Starts receiving changes made by other members (after login). Throws if the server can't be reached.</summary>
		Task StartLiveUpdatesAsync();

		/// <summary>Stops receiving changes (on logout).</summary>
		Task StopLiveUpdatesAsync();

		/// <summary>
		/// The logged-in member's joined group: the one they follow live. If they haven't joined one (or
		/// it's gone), their first group is joined. Null if they're in no group.
		/// </summary>
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

		/// <summary>
		/// Makes the group the member's current one (they join it), also for later logins. Joining a
		/// band one is invited to accepts the invitation.
		/// </summary>
		Task SelectGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>Finds a member to invite by username, email or phone; null if there's none. Needs the server.</summary>
		Task<FoundMember?> FindMemberAsync(string query, CancellationToken cancellationToken = default);

		/// <summary>Invites a member to a band (as a plain member). Admins only; the server refuses others.</summary>
		Task InviteMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Emails someone who has no account yet, asking them to create one to join the band. They
		/// aren't in the band by this: once they have an account they're invited as any member.
		/// Admins only. Needs the server; throws (with why) if the email can't be sent.
		/// </summary>
		Task InviteByEmailAsync(Guid groupId, string email, CancellationToken cancellationToken = default);

		/// <summary>
		/// Makes an account for someone new, for a band (its admins only): sent their username and how
		/// to set their own password, by email, or by text when a phone number is given instead (no @).
		/// Their language ("sv" or "en") is kept as their language setting, and they're told in it.
		/// Returns the account, to invite to the band (see <see cref="InviteMemberAsync"/>). Needs the
		/// server; throws (with why) if it's refused.
		/// </summary>
		Task<FoundMember> CreateMemberAsync(Guid groupId, string userName, string name, string emailOrPhone, string language, CancellationToken cancellationToken = default);

		/// <summary>Changes a member's role in a band. Admins only, and the last admin stays one.</summary>
		Task SetMemberRoleAsync(Guid groupId, Guid memberId, GroupRole role, CancellationToken cancellationToken = default);

		/// <summary>Changes a bandmate's name (everywhere, not just in one band). Admins of a band they're in only; the server refuses others.</summary>
		Task RenameMemberAsync(Guid memberId, string name, CancellationToken cancellationToken = default);

		/// <summary>Changes the logged-in member's own name (everywhere), saved here and sent to the server like other changes.</summary>
		Task RenameMeAsync(string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// The logged-in member's own settings, the same on all their devices (e.g. the colours): a JSON
		/// object, null if they've never saved any. Needs the server: throws if it can't be reached.
		/// </summary>
		Task<string?> GetMySettingsAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// The release notes on the server, newest first: the published ones (a site manager also gets the
		/// unpublished ones). Needs the server: throws if it can't be reached.
		/// </summary>
		Task<IReadOnlyList<ReleaseNoteEntry>> GetReleaseNotesAsync(CancellationToken cancellationToken = default);

		/// <summary>Adds the release note, or replaces the stored one with the same id. Site managers only; throws otherwise or offline.</summary>
		Task SaveReleaseNoteAsync(ReleaseNoteEntry note, CancellationToken cancellationToken = default);

		/// <summary>Every member the logged-in member can see (a site manager: all of them), by name.</summary>
		Task<IReadOnlyList<SiteMember>> GetAllMembersAsync(CancellationToken cancellationToken = default);

		/// <summary>The members a site manager has disabled (they can't log in). Site managers only; throws otherwise or offline.</summary>
		Task<IReadOnlySet<Guid>> GetDisabledMembersAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Disables a member's account (they can't log in or use it) or enables it again. Site managers
		/// only, and not their own account; throws otherwise or offline.
		/// </summary>
		Task SetMemberDisabledAsync(Guid memberId, bool disabled, CancellationToken cancellationToken = default);

		/// <summary>Another member's own settings (a JSON object; null if none), for a site manager. Needs the server.</summary>
		Task<string?> GetMemberSettingsAsync(Guid memberId, CancellationToken cancellationToken = default);

		/// <summary>Replaces another member's own settings (a JSON object), as a site manager. Needs the server.</summary>
		Task SaveMemberSettingsAsync(Guid memberId, string settings, CancellationToken cancellationToken = default);

		/// <summary>Replaces the logged-in member's own settings (a JSON object). Needs the server: throws if it can't be reached.</summary>
		Task SaveMySettingsAsync(string settings, CancellationToken cancellationToken = default);

		/// <summary>
		/// Raised (on a background thread) with the member's own settings (a JSON object) when they're
		/// changed on another of their devices. Only while live updates are started.
		/// </summary>
		event Action<string>? MySettingsChanged;

		/// <summary>The logged-in member's own settings for the song (no transpose and no lyrics of their own if none).</summary>
		Task<MySongSetting> GetMySongSettingAsync(Guid songId, CancellationToken cancellationToken = default);

		/// <summary>Stores the logged-in member's own settings for the song; works offline and syncs later.</summary>
		Task SaveMySongSettingAsync(Guid songId, MySongSetting setting, CancellationToken cancellationToken = default);

		/// <summary>
		/// Raised (possibly on a background thread) with the song's id when the member's own settings for
		/// it change on another of their devices.
		/// </summary>
		event Action<Guid>? MySongSettingChanged;

		/// <summary>
		/// The other members of the band and their own lyrics for the song (empty if they have none),
		/// for the band's admins; empty for anyone else.
		/// </summary>
		Task<IReadOnlyList<MemberLyrics>> GetMembersLyricsAsync(Guid groupId, Guid songId, CancellationToken cancellationToken = default);

		/// <summary>Stores another member's own lyrics for the song (empty removes them). The band's admins only.</summary>
		Task SaveMemberLyricsAsync(Guid songId, Guid memberId, string lyrics, CancellationToken cancellationToken = default);

		/// <summary>Removes a member from a band. Admins only, and not the last admin.</summary>
		Task RemoveMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken = default);

		/// <summary>Deletes a band (the API keeps it, marked as removed). Admins only.</summary>
		Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

		Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>The setlist, or null if it doesn't exist (any more).</summary>
		Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default);

		Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// What's new or changed in the member's bands since then, newest first: bands they were added
		/// to, new members, new or changed setlists and songs. Changes made on this device (since
		/// <see cref="ForgetLocalChanges"/>) are left out.
		/// </summary>
		Task<IReadOnlyList<Change>> GetChangesSinceAsync(DateTime since, CancellationToken cancellationToken = default);

		/// <summary>Forgets which changes were made on this device, once the changes since the last visit have been shown.</summary>
		void ForgetLocalChanges();

		/// <summary>Gives the band a new name (not the private band's, which is fixed).</summary>
		Task RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default);

		/// <summary>Replaces the band's MIDI expressions (see <see cref="Midi"/>). Admins only; the server refuses others.</summary>
		Task SetMidiExpressionsAsync(Guid groupId, IReadOnlyList<MidiExpression> expressions, CancellationToken cancellationToken = default);

		/// <summary>Replaces the band's MIDI commands for every song (select, start, stop). Admins only; the server refuses others.</summary>
		Task SetMidiSongCommandsAsync(Guid groupId, MidiSongCommands commands, CancellationToken cancellationToken = default);

		/// <summary>
		/// Replaces the band's lyrics header (see <see cref="LyricsHeader"/>): null for the default, empty
		/// for none. Admins only; the server refuses others.
		/// </summary>
		Task SetLyricsHeaderAsync(Guid groupId, string? header, CancellationToken cancellationToken = default);

		/// <summary>Sets whether stopping a song at the end of its lyrics goes on to the next song, for the band: null (unset) is yes. Admins only; the server refuses others.</summary>
		Task SetNextSongOnStopAtEndAsync(Guid groupId, bool? goOn, CancellationToken cancellationToken = default);

		/// <summary>Gives the setlist a new name.</summary>
		Task RenameSetlistAsync(Guid setlistId, string name, CancellationToken cancellationToken = default);

		/// <summary>Deletes the setlist; its songs stay in the songbook.</summary>
		Task DeleteSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>The setlist's songs in order.</summary>
		Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>One setlist entry with its song, or null if it doesn't exist (any more).</summary>
		Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default);

		/// <summary>The songs in the group's songbooks, by name.</summary>
		Task<IReadOnlyList<SongDetails>> GetSongbookSongsAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>The band's playlogs, newest first, each with its songs in the order they were played.</summary>
		Task<IReadOnlyList<PlaylogSummary>> GetPlaylogsAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Adds the song to the band's playlog for today (started if there's none yet), with its key
		/// and tempo and this member as conductor, and returns the play; null if the song is gone. If
		/// the song is already the last one in it, that play is returned instead (playing on after a stop).
		/// </summary>
		/// <param name="setlistId">The setlist it's played from, kept with the play for the sessions; null for none.</param>
		Task<Guid?> AddPlayAsync(Guid groupId, Guid songId, Guid? setlistId = null, CancellationToken cancellationToken = default);

		/// <summary>Adds to how long the song was played.</summary>
		Task AddPlayTimeAsync(Guid playId, TimeSpan time, CancellationToken cancellationToken = default);

		/// <summary>Keeps the band's recording of the play with it: a sound file's media link (see <see cref="MediaLinks.SoundLink"/>).</summary>
		Task SetPlayRecordingAsync(Guid playId, string recording, CancellationToken cancellationToken = default);

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

		/// <summary>Makes the setlist's songs exactly these, in this order (its earlier songs are taken out); the setlist itself stays.</summary>
		Task ReplaceSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default);

		/// <summary>Puts the setlist's songs in this order (by their entries, which stay the same, so the band's current song does too).</summary>
		Task MoveSongsAsync(Guid setlistId, IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// A complete export of the server's data, for a site manager logged in elevated: members (without
		/// what logs them in), bands, memberships, songbooks, songs, setlists, song settings, sessions and
		/// release notes; as JSON, or as a SQLite database (<paramref name="sqlite"/>).
		/// </summary>
		Task<byte[]> ExportAllDataAsync(bool sqlite, CancellationToken cancellationToken = default);

		/// <summary>Which version the connected server runs; null when it doesn't say (a server older than this).</summary>
		Task<ServerVersion?> GetServerVersionAsync(CancellationToken cancellationToken = default);

		/// <summary>Makes the setlist the group's current one (and clears the current song).</summary>
		Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>Makes the entry the group's current song (and its setlist the current setlist).</summary>
		Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sends a sound file kept on this device to the server, for the song's band (see
		/// <see cref="SoundSync"/>). Throws when the server can't be reached or doesn't take it yet
		/// (e.g. the song hasn't been sent to it).
		/// </summary>
		Task UploadSoundAsync(Guid songId, string soundId, System.IO.Stream content, CancellationToken cancellationToken = default);

		/// <summary>A sound file from the server; null when it doesn't have it. Throws when it can't be reached.</summary>
		Task<System.IO.Stream?> DownloadSoundAsync(string soundId, CancellationToken cancellationToken = default);
	}

	/// <param name="Online">Whether the server could be reached last time it was tried.</param>
	/// <param name="PendingChanges">Changes made on this device that the server hasn't got yet.</param>
	/// <param name="LastSynced">When this device last got a full copy from the server; null if never.</param>
	/// <param name="Problem">The last sync problem worth telling the member (e.g. a change the server refused).</param>
	public sealed record SyncStatus(bool Online, int PendingChanges, DateTime? LastSynced, string? Problem);

	/// <summary>What a <see cref="Change"/> is about.</summary>
	public enum ChangeKind { Band, Member, Setlist, Song }

	/// <summary>
	/// Something new or changed in the member's bands (see <see cref="ISetlistService.GetChangesSinceAsync"/>):
	/// a band they were added to, a new member, a new or changed setlist or song.
	/// </summary>
	/// <param name="Name">The band's, member's, setlist's or song's name.</param>
	/// <param name="Band">The band it's in.</param>
	/// <param name="IsNew">New (else changed).</param>
	public sealed record Change(ChangeKind Kind, string Name, string Band, bool IsNew, DateTime When);

	/// <param name="MidiExpressions">The band's MIDI expressions as stored (a JSON array, see <see cref="Midi.ExpressionsFromJson"/>); null if none.</param>
	/// <param name="MidiCommands">The band's MIDI commands for every song as stored (see <see cref="Midi.SongCommandsFromJson"/>); null if none.</param>
	/// <param name="NextSongOnStopAtEnd">Whether stopping a song at the end of its lyrics goes on to the next song; null (unset) is yes.</param>
	/// <param name="LyricsHeader">The header above every song's lyrics as stored (see <see cref="Setlists.LyricsHeader"/>): null for the default, empty for none.</param>
	public sealed record GroupSummary(Guid Id, string Name, Guid? CurrentSetlistId, Guid? CurrentSetlistItemId, bool IsPrivate = false, string? MidiExpressions = null, string? MidiCommands = null, string? LyricsHeader = null, bool? NextSongOnStopAtEnd = null);

	public enum GroupRole { Member, Conductor, Admin }

	/// <summary>
	/// A member of a group: their role, whether they have accepted the invitation yet, when they last
	/// logged in (null if not known), whether it's the logged-in member, whether they've joined this
	/// group (it's their current one), and whether their app is connected right now.
	/// </summary>
	public sealed record GroupMember(Guid MemberId, string Name, GroupRole Role, bool Approved, DateTime? LastLogin = null, bool IsMe = false, bool Joined = false, bool Connected = false);

	/// <summary>A release note kept on the server: what a release brought, its version and branch, whether it's published, and when it was made.</summary>
	public sealed record ReleaseNoteEntry(Guid Id, string Text, string Branch, string Version, bool Published, DateTime Created);

	/// <summary>A server's version as its build recorded it (e.g. "0.1.14.0"), with the git branch and commit when known.</summary>
	public sealed record ServerVersion(string Version, string? Branch, string? Commit);

	/// <summary>A member as the site manager's page lists them.</summary>
	public sealed record SiteMember(Guid Id, string Name, string UserName, DateTime? LastLogin);

	/// <summary>A member found to invite.</summary>
	public sealed record FoundMember(Guid Id, string Name, string UserName);

	public sealed record SetlistSummary(Guid Id, string Name, Guid GroupId);

	/// <param name="MediaLink">A link to listen to or watch the song (Spotify, YouTube, …); empty if none.</param>
	/// <param name="Tags">Tags to sort the song by, separated by spaces (see <see cref="SongTags"/>); empty if none.</param>
	/// <summary>
	/// A member's own settings for a song, seen only by them: semitones the chords are moved by on
	/// top of the song's own (−11 to +11), and their own lyrics (empty: the band's are shown).
	/// </summary>
	/// <param name="MidiSound">The sound the member's keyboard plays the song with (see <see cref="Lyrious.UILib.MidiSound"/>); empty for none.</param>
	public sealed record MySongSetting(int Transpose, string Lyrics, bool TransposeOff = false, string MidiSound = "")
	{
		/// <summary>The semitones the chords are moved by now: none while the transpose is turned off.</summary>
		public int ActiveTranspose => TransposeOff ? 0 : Transpose;
		public const int MinTranspose = -11;
		public const int MaxTranspose = 11;
		public static readonly MySongSetting None = new(0, "");
		public bool HasLyrics => !string.IsNullOrWhiteSpace(Lyrics);
	}

	/// <summary>A band member and their own lyrics for a song (empty: they see the band's).</summary>
	public sealed record MemberLyrics(Guid MemberId, string Name, string Lyrics);

	public sealed record SongDetails(Guid Id, string Name, string PlayingKey, float Tempo, string Beat, string Lyrics, string MediaLink = "", string Tags = "", string OriginalKey = "", string Artist = "");

	/// <summary>A song found online: title, artist, album, length and plain lyrics (empty if the database has none).</summary>
	public sealed record InternetSong(string Title, string Artist, string Album, TimeSpan Duration, string Lyrics, bool Instrumental);

	/// <summary>A band's playlog: the songs played on one occasion (e.g. a gig or a rehearsal), in order.</summary>
	public sealed record PlaylogSummary(Guid Id, DateTime CreatedAt, IReadOnlyList<PlayEntry> Plays);

	/// <summary>
	/// A song played: when, in which key and tempo, for how long (zero if unknown), who conducted it
	/// (empty if unknown), the setlist it was played from (null if none or unknown), and the band's
	/// recording of it (a sound file's media link; null if none).
	/// </summary>
	public sealed record PlayEntry(Guid Id, DateTime PlayedAt, Guid SongId, string SongName, string Key, float Tempo, TimeSpan Duration, string Conductor, Guid? SetlistId = null, string? Recording = null);

	/// <summary>A song at a position in a setlist.</summary>
	public sealed record SetlistEntry(Guid Id, Guid SetlistId, int Position, SongDetails Song);

	/// <summary>Used until a host provides a real service, so the pages say why nothing loads.</summary>
	public sealed class UnconfiguredSetlistService : ISetlistService
	{
		private static Exception NotConnected() => new InvalidOperationException(L.T("This app isn't connected to a Lyrious server yet."));

		public SyncStatus Status => new(false, 0, null, null);
		public event Action? StatusChanged { add { } remove { } }
		public Task SyncNowAsync() => Task.CompletedTask;
		public event Action<GroupSummary>? GroupChanged { add { } remove { } }
		public event Action<Guid>? JoinedGroupChanged { add { } remove { } }
		public event Action<Guid, bool>? PlaybackChanged { add { } remove { } }
		public event Action<Guid, double>? PlaybackPositionChanged { add { } remove { } }
		public event Action<Guid, bool>? MemberConnectionChanged { add { } remove { } }
		public event Action? MembersChanged { add { } remove { } }
		public Task SendPlaybackAsync(bool playing) => Task.CompletedTask;
		public Task SendPlaybackPositionAsync(double position) => Task.CompletedTask;
		public Task StartLiveUpdatesAsync() => Task.CompletedTask;
		public Task StopLiveUpdatesAsync() => Task.CompletedTask;

		public Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>>> GetGroupMembersAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<GroupSummary> CreateGroupAsync(string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<FoundMember?> FindMemberAsync(string query, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task InviteMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task InviteByEmailAsync(Guid groupId, string email, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<FoundMember> CreateMemberAsync(Guid groupId, string userName, string name, string emailOrPhone, string language, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetMemberRoleAsync(Guid groupId, Guid memberId, GroupRole role, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task RenameMemberAsync(Guid memberId, string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task RenameMeAsync(string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<string?> GetMySettingsAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SaveMySettingsAsync(string settings, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SiteMember>> GetAllMembersAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlySet<Guid>> GetDisabledMembersAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetMemberDisabledAsync(Guid memberId, bool disabled, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<ReleaseNoteEntry>> GetReleaseNotesAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SaveReleaseNoteAsync(ReleaseNoteEntry note, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<string?> GetMemberSettingsAsync(Guid memberId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SaveMemberSettingsAsync(Guid memberId, string settings, CancellationToken cancellationToken = default) => throw NotConnected();
		public event Action<string>? MySettingsChanged { add { } remove { } }
		public Task<MySongSetting> GetMySongSettingAsync(Guid songId, CancellationToken cancellationToken = default) => Task.FromResult(MySongSetting.None);
		public Task SaveMySongSettingAsync(Guid songId, MySongSetting setting, CancellationToken cancellationToken = default) => throw NotConnected();
		public event Action<Guid>? MySongSettingChanged { add { } remove { } }
		public Task<IReadOnlyList<MemberLyrics>> GetMembersLyricsAsync(Guid groupId, Guid songId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MemberLyrics>>([]);
		public Task SaveMemberLyricsAsync(Guid songId, Guid memberId, string lyrics, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task RemoveMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<Change>> GetChangesSinceAsync(DateTime since, CancellationToken cancellationToken = default) => throw NotConnected();
		public void ForgetLocalChanges() { }
		public Task RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetMidiExpressionsAsync(Guid groupId, IReadOnlyList<MidiExpression> expressions, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetMidiSongCommandsAsync(Guid groupId, MidiSongCommands commands, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetLyricsHeaderAsync(Guid groupId, string? header, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetNextSongOnStopAtEndAsync(Guid groupId, bool? goOn, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task RenameSetlistAsync(Guid setlistId, string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task DeleteSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SongDetails>> GetSongbookSongsAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<PlaylogSummary>> GetPlaylogsAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<Guid?> AddPlayAsync(Guid groupId, Guid songId, Guid? setlistId = null, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task AddPlayTimeAsync(Guid playId, TimeSpan time, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SetPlayRecordingAsync(Guid playId, string recording, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<InternetSong>> SearchInternetAsync(string query, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SongDetails> CreateSongAsync(Guid groupId, string name, string lyrics, TimeSpan duration, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task DeleteSongAsync(Guid songId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SongDetails> UpdateSongAsync(SongDetails song, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SongDetails> UpdateLyricsAsync(Guid songId, string lyrics, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task AddSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task ReplaceSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task MoveSongsAsync(Guid setlistId, IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<byte[]> ExportAllDataAsync(bool sqlite, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<ServerVersion?> GetServerVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult<ServerVersion?>(null);
		public Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task UploadSoundAsync(Guid songId, string soundId, System.IO.Stream content, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<System.IO.Stream?> DownloadSoundAsync(string soundId, CancellationToken cancellationToken = default) => throw NotConnected();
	}
}
