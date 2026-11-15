using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.Client.Offline;
using Lyrious.UILib;
using Lyrious.UILib.Setlists;

namespace Lyrious.Client;

/// <summary>
/// Setlist and song data for the logged-in member, offline-first. Everything is read from a cache on
/// this device (<see cref="EntityCache"/>), so the app works without the server; changes go into the
/// cache at once and are queued, and <see cref="SyncEngine"/> sends them when the server can be
/// reached and fetches what others changed. While online, live updates keep the cache current.
/// The API only returns (and accepts) what the member may see.
/// </summary>
public sealed class HttpSetlistService : ISetlistService
{
    private readonly HttpAuthService auth;
    private readonly ISettingsStore store;
    private readonly LiveUpdates live;
    private readonly SyncEngine engine;
    private EntityCache? cache;
    private bool liveWanted;

    /// <param name="store">Where the cache is kept on this device; without one, it lasts only while the app runs.</param>
    public HttpSetlistService(HttpAuthService auth, ISettingsStore? store = null)
    {
        this.auth = auth;
        this.store = store ?? new MemorySettingsStore();
        live = new LiveUpdates(auth);
        engine = new SyncEngine(auth, () => live.ConnectionId);

        live.EntitiesChanged += OnServerChanged;
        live.PlaybackChanged += (groupId, playing) => PlaybackChanged?.Invoke(groupId, playing);
        live.Reconnected += () => { if (cache is { } c) _ = engine.SyncAsync(c); };
        live.Lost += () => { if (cache is { } c && liveWanted) engine.ReportOffline(c); };
        engine.StatusChanged += () => StatusChanged?.Invoke();
        engine.Downloaded += OnDownloaded;
        engine.BackOnline += () => _ = StartLiveIfWantedAsync();

        // Every login (online or offline) syncs straight away: sends what's waiting, fetches what's new.
        auth.LoggedIn += () => _ = CacheAsync();
    }

    public event Action<GroupSummary>? GroupChanged;

    public event Action<Guid>? JoinedGroupChanged;

    public event Action<Guid, bool>? PlaybackChanged;

    public event Action? StatusChanged;

    public SyncStatus Status => new(engine.Online, cache?.PendingCount ?? 0, cache?.LastSynced, engine.Problem);

    public Task SendPlaybackAsync(bool playing) => live.SetPlaybackAsync(playing);

    /// <summary>Starts live updates now if the server can be reached, else as soon as it can.</summary>
    public async Task StartLiveUpdatesAsync()
    {
        liveWanted = true;
        await StartLiveIfWantedAsync();
    }

    public async Task StopLiveUpdatesAsync()
    {
        liveWanted = false;
        engine.Stop();
        await live.StopAsync();
    }

    public async Task SyncNowAsync()
    {
        if (cache is { } c)
        {
            await engine.SyncAsync(c);
            await StartLiveIfWantedAsync();
        }
    }

    private async Task StartLiveIfWantedAsync()
    {
        if (!liveWanted || !engine.Online || string.IsNullOrEmpty(auth.AccessToken) || live.IsConnected)
        {
            return;
        }

        try
        {
            await live.StartAsync();
        }
        catch (Exception ex) when (HttpAuthService.IsNetworkFailure(ex) || ex is HttpRequestException)
        {
            System.Diagnostics.Debug.WriteLine($"Live updates aren't available: {ex.Message}");
        }
    }

    // The cache, ready (loaded and synced once) for the login session it was made ready for.
    private Task<EntityCache>? ready;
    private int readySession = -1;

    /// <summary>
    /// The logged-in member's cache, loaded from this device. After each login it's synced with the
    /// server first (if it can be reached; if not, the copy on the device is used and syncing is
    /// tried again every few seconds). Callers during that first sync wait for the same one.
    /// </summary>
    private Task<EntityCache> CacheAsync()
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException("You're not logged in.");
        if (ready == null || readySession != auth.Session || cache?.MemberId != memberId)
        {
            readySession = auth.Session;
            ready = LoadAndSyncAsync(memberId);
        }

        return ready;
    }

    private async Task<EntityCache> LoadAndSyncAsync(Guid memberId)
    {
        if (cache?.MemberId != memberId)
        {
            cache = EntityCache.Load(store, memberId);
            StatusChanged?.Invoke();
        }

        var current = cache;
        try
        {
            await engine.SyncAsync(current);
        }
        catch (Exception ex)
        {
            // An unexpected sync problem mustn't lock the member out of their own copy.
            System.Diagnostics.Debug.WriteLine($"Sync after login failed: {ex.Message}");
        }

        return current;
    }

    /// <summary>Records a change made here and sends it in the background (now if online, later if not).</summary>
    private void Queued(EntityCache current)
    {
        _ = engine.PushAsync(current);
    }

    private void OnServerChanged(string type, JsonObject[] entities)
    {
        if (cache is not { } current)
        {
            return;
        }

        var shown = current.Merge(type, entities);
        if (type == "group")
        {
            foreach (var group in shown)
            {
                GroupChanged?.Invoke(ToSummary(As(group, ClientJsonContext.Default.GroupDto)));
            }
        }
        else if (type == "member")
        {
            FollowJoinedGroup(current);
        }
    }

    /// <summary>
    /// If the member joined another group in another session (the cache's copy of the member, with
    /// any change made here still on top, says so), this session joins it too.
    /// </summary>
    private void FollowJoinedGroup(EntityCache current)
    {
        if (auth.MemberId is not { } memberId
            || current.Find("member", memberId) is not { } me
            || !Guid.TryParse((string?)me["joinedGroupId"], out var joined)
            || joined == auth.JoinedGroupId)
        {
            return;
        }

        auth.SetJoinedGroup(joined);
        JoinedGroupChanged?.Invoke(joined);
    }

    /// <summary>After a full download, tell the app about groups whose current setlist or song changed meanwhile.</summary>
    private void OnDownloaded(IReadOnlyDictionary<Guid, JsonObject> groupsBefore)
    {
        if (cache is not { } current)
        {
            return;
        }

        // Joined another group in another session while this one was offline or not listening?
        FollowJoinedGroup(current);

        foreach (var group in current.All("group").Select(g => As(g, ClientJsonContext.Default.GroupDto)))
        {
            if (groupsBefore.TryGetValue(group.Id, out var before)
                && As(before, ClientJsonContext.Default.GroupDto) is var old
                && (old.CurrentSetlistId != group.CurrentSetlistId || old.CurrentSetlistItemId != group.CurrentSetlistItemId))
            {
                GroupChanged?.Invoke(ToSummary(group));
            }
        }
    }

    // ---- Reading (from the cache) ----

    private static T As<T>(JsonObject entity, JsonTypeInfo<T> type) => entity.Deserialize(type)!;

    private static IEnumerable<T> Live<T>(EntityCache cache, string type, JsonTypeInfo<T> info)
        => cache.All(type).Where(entity => !EntityCache.IsRemoved(entity)).Select(entity => As(entity, info));

    private static T? Find<T>(EntityCache cache, string type, Guid id, JsonTypeInfo<T> info) where T : class
        => cache.Find(type, id) is { } entity && !EntityCache.IsRemoved(entity) ? As(entity, info) : null;

    public async Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default)
    {
        var groups = await GetGroupsAsync(cancellationToken);
        if (groups.FirstOrDefault(group => group.Id == auth.JoinedGroupId) is { } joined)
        {
            return joined;
        }

        // Not joined to any (existing) group: join the first, so the band shown is the one followed live.
        if (groups.FirstOrDefault() is not { } first)
        {
            return null;
        }

        await SelectGroupAsync(first.Id, cancellationToken);
        return first;
    }

    public async Task<IReadOnlyList<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        return Live(current, "group", ClientJsonContext.Default.GroupDto)
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>>> GetGroupMembersAsync(CancellationToken cancellationToken = default)
    {
        const int approved = 2;
        var current = await CacheAsync();
        var names = current.All("member")
            .Select(member => As(member, ClientJsonContext.Default.MemberDto))
            .ToDictionary(member => member.Id, member => !string.IsNullOrWhiteSpace(member.Name) ? member.Name : member.UserName ?? "Unknown");

        // Admins first, then conductors, then members; each by name.
        return Live(current, "membership", ClientJsonContext.Default.MembershipDto)
            .Select(membership => (membership.GroupId, Member: new GroupMember(
                membership.MemberId,
                names.GetValueOrDefault(membership.MemberId, "Unknown"),
                Enum.IsDefined(typeof(GroupRole), membership.RoleEnum) ? (GroupRole)membership.RoleEnum : GroupRole.Member,
                membership.MembershipStatusEnum == approved)))
            .GroupBy(entry => entry.GroupId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GroupMember>)group
                    .Select(entry => entry.Member)
                    .OrderByDescending(member => member.Role)
                    .ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray());
    }

    public async Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        return Live(current, "setlist", ClientJsonContext.Default.SetlistDto)
            .Where(setlist => setlist.GroupId == groupId)
            .OrderBy(setlist => setlist.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        return Find(current, "setlist", setlistId, ClientJsonContext.Default.SetlistDto) is { } setlist ? ToSummary(setlist) : null;
    }

    public async Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        var songs = Live(current, "song", ClientJsonContext.Default.SongDto).ToDictionary(song => song.Id);
        return Live(current, "setlistitem", ClientJsonContext.Default.SetlistItemDto)
            .Where(item => item.SetlistId == setlistId && songs.ContainsKey(item.SongId))
            .OrderBy(item => item.Position)
            .Select(item => ToEntry(item, songs[item.SongId]))
            .ToArray();
    }

    public async Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        if (Find(current, "setlistitem", setlistItemId, ClientJsonContext.Default.SetlistItemDto) is not { } item)
        {
            return null;
        }

        return Find(current, "song", item.SongId, ClientJsonContext.Default.SongDto) is { } song ? ToEntry(item, song) : null;
    }

    public async Task<IReadOnlyList<SongDetails>> GetSongbookSongsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        var songbookIds = Live(current, "songbook", ClientJsonContext.Default.SongbookDto)
            .Where(songbook => songbook.GroupId == groupId)
            .Select(songbook => songbook.Id)
            .ToHashSet();

        return Live(current, "song", ClientJsonContext.Default.SongDto)
            .Where(song => songbookIds.Contains(song.SongbookId))
            .OrderBy(song => song.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToDetails)
            .ToArray();
    }

    public async Task<IReadOnlyList<PlaylogSummary>> GetPlaylogsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        var songs = current.All("song").Select(song => As(song, ClientJsonContext.Default.SongDto)).ToDictionary(song => song.Id);
        var members = current.All("member")
            .Select(member => As(member, ClientJsonContext.Default.MemberDto))
            .ToDictionary(member => member.Id, member => !string.IsNullOrWhiteSpace(member.Name) ? member.Name : member.UserName ?? "");
        var plays = Live(current, "play", ClientJsonContext.Default.PlayDto)
            .ToLookup(play => play.PlaylogId);

        // Newest first; each playlog's songs in the order they were played.
        return Live(current, "playlog", ClientJsonContext.Default.PlaylogDto)
            .Where(playlog => playlog.GroupId == groupId)
            .OrderByDescending(playlog => playlog.CreatedAt)
            .Select(playlog => new PlaylogSummary(
                playlog.Id,
                playlog.CreatedAt,
                plays[playlog.Id]
                    .OrderBy(play => play.CreatedAt)
                    .Select(play => new PlayEntry(
                        play.Id,
                        play.CreatedAt,
                        play.SongId,
                        // A song deleted since keeps its name in the cache (removed), so it can still be named.
                        songs.TryGetValue(play.SongId, out var song) ? song.Name : "Unknown song",
                        play.KeyString ?? "",
                        play.Tempo,
                        TimeSpan.FromMilliseconds(play.DurationMilliSeconds),
                        members.GetValueOrDefault(play.ConductorId, "")))
                    .ToArray()))
            .ToArray();
    }

    // ---- Changing (into the cache, then sent) ----

    public async Task<Guid?> AddPlayAsync(Guid groupId, Guid songId, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException("You're not logged in.");
        var current = await CacheAsync();
        if (Find(current, "song", songId, ClientJsonContext.Default.SongDto) is not { } song)
        {
            return null;
        }

        // Today's playlog for the band (a new one each day, as the API's own PlayAsync does).
        var now = DateTime.Now;
        var playlog = Live(current, "playlog", ClientJsonContext.Default.PlaylogDto)
            .Where(log => log.GroupId == groupId && log.CreatedAt.Date == now.Date)
            .MaxBy(log => log.CreatedAt);
        if (playlog == null)
        {
            playlog = new PlaylogDto(Guid.NewGuid(), groupId, now, now, Removed: false);
            current.Create("playlog", Node(playlog, ClientJsonContext.Default.PlaylogDto));
        }
        else if (Live(current, "play", ClientJsonContext.Default.PlayDto)
                     .Where(play => play.PlaylogId == playlog.Id)
                     .MaxBy(play => play.CreatedAt) is { } last
                 && last.SongId == songId)
        {
            // Playing on after a stop: still the same play of the song.
            return last.Id;
        }

        var newPlay = new PlayDto(Guid.NewGuid(), playlog.Id, songId, memberId, song.Key ?? "", song.Tempo, 0, now, now, Removed: false);
        current.Create("play", Node(newPlay, ClientJsonContext.Default.PlayDto));
        Queued(current);
        return newPlay.Id;
    }

    public async Task AddPlayTimeAsync(Guid playId, TimeSpan time, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        if (Find(current, "play", playId, ClientJsonContext.Default.PlayDto) is not { } play || time <= TimeSpan.Zero)
        {
            return;
        }

        var total = (long)play.DurationMilliSeconds + (long)time.TotalMilliseconds;
        current.Update("play", playId, new JsonObject { ["durationMilliSeconds"] = (int)Math.Min(total, int.MaxValue) });
        Queued(current);
    }

    private static JsonObject Node<T>(T value, JsonTypeInfo<T> type) => (JsonObject)JsonSerializer.SerializeToNode(value, type)!;

    public async Task<GroupSummary> CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException("You're not logged in.");
        var current = await CacheAsync();
        const int admin = 2, approved = 2;

        // The order matters (the outbox keeps it): the API accepts the creator's membership only once
        // the group exists, and everything else in the group only from its members. Ids are chosen here.
        var now = DateTime.Now;
        var group = new GroupDto(Guid.NewGuid(), name, CurrentSetlistId: null, CurrentPlayId: null, CurrentSetlistItemId: null, now, now, Removed: false, Checksum: 0);
        current.Create("group", Node(group, ClientJsonContext.Default.GroupDto));
        current.Create("membership", Node(new MembershipDto(Guid.NewGuid(), admin, approved, group.Id, memberId, Removed: false), ClientJsonContext.Default.MembershipDto));
        current.Create("songbook", Node(new SongbookDto(Guid.NewGuid(), "Default", group.Id, Removed: false), ClientJsonContext.Default.SongbookDto));

        var setlist = new SetlistDto(Guid.NewGuid(), StartPage.DefaultSetlistName, group.Id, now, now, Removed: false, Checksum: 0);
        current.Create("setlist", Node(setlist, ClientJsonContext.Default.SetlistDto));
        current.Update("group", group.Id, new JsonObject { ["currentSetlistId"] = setlist.Id.ToString() });
        Queued(current);

        return new GroupSummary(group.Id, group.Name, setlist.Id, null);
    }

    /// <summary>Joins the group by changing the member's joined group.</summary>
    public async Task SelectGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException("You're not logged in.");
        var current = await CacheAsync();
        current.Update("member", memberId, new JsonObject { ["joinedGroupId"] = groupId.ToString() });
        auth.SetJoinedGroup(groupId);
        Queued(current);
    }

    public async Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        var now = DateTime.Now;
        var setlist = new SetlistDto(Guid.NewGuid(), name, groupId, now, now, Removed: false, Checksum: 0);
        current.Create("setlist", Node(setlist, ClientJsonContext.Default.SetlistDto));
        Queued(current);
        return ToSummary(setlist);
    }

    /// <summary>Deletes the setlist (the API keeps it, marked as removed). Its songs stay in the songbook.</summary>
    public async Task DeleteSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Delete("setlist", setlistId);
        Queued(current);
    }

    public async Task<SongDetails> CreateSongAsync(Guid groupId, string name, string lyrics, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        var songbook = Live(current, "songbook", ClientJsonContext.Default.SongbookDto)
                       .Where(book => book.GroupId == groupId)
                       .OrderBy(book => book.Name, StringComparer.CurrentCultureIgnoreCase)
                       .FirstOrDefault()
                       ?? throw new InvalidOperationException("The band has no songbook.");

        // The server keeps at most 100 characters of name and 5000 of lyrics.
        var now = DateTime.Now;
        var song = new NewSongDto(
            Guid.NewGuid(),
            Limit(name, 100),
            Key: "",
            Tempo: 120,
            Beat: "4/4",
            LengthMilliSeconds: duration > TimeSpan.Zero ? (int)duration.TotalMilliseconds : (int)TimeSpan.FromMinutes(3).TotalMilliseconds,
            Lyrics: Limit(lyrics, 5000),
            songbook.Id, now, now, Removed: false);
        current.Create("song", Node(song, ClientJsonContext.Default.NewSongDto));
        Queued(current);
        return new SongDetails(song.Id, song.Name, song.Key, song.Tempo, song.Beat, song.Lyrics, song.MediaLink);
    }

    /// <summary>
    /// Deletes the song. The API keeps it, marked as removed; setlist entries that point to it are
    /// left as they are and skipped when a setlist is read (<see cref="GetEntriesAsync"/>).
    /// </summary>
    public async Task DeleteSongAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Delete("song", songId);
        Queued(current);
    }

    public async Task<SongDetails> UpdateSongAsync(SongDetails song, CancellationToken cancellationToken = default)
    {
        // The server's limits: name 100 characters, key and beat 10, lyrics 5000, media link 500.
        return await UpdateSongFieldsAsync(song.Id, new JsonObject
        {
            ["name"] = Limit(song.Name, 100),
            ["key"] = Limit(song.Key, 10),
            ["tempo"] = song.Tempo,
            ["beat"] = Limit(song.Beat, 10),
            ["lyrics"] = Limit(song.Lyrics, 5000),
            ["mediaLink"] = Limit(song.MediaLink, 500),
        });
    }

    public Task<SongDetails> UpdateLyricsAsync(Guid songId, string lyrics, CancellationToken cancellationToken = default)
        => UpdateSongFieldsAsync(songId, new JsonObject { ["lyrics"] = Limit(lyrics, 5000) });

    private async Task<SongDetails> UpdateSongFieldsAsync(Guid songId, JsonObject fields)
    {
        var current = await CacheAsync();
        if (Find(current, "song", songId, ClientJsonContext.Default.SongDto) == null)
        {
            throw new InvalidOperationException("The song no longer exists.");
        }

        current.Update("song", songId, fields);
        Queued(current);
        return ToDetails(Find(current, "song", songId, ClientJsonContext.Default.SongDto)!);
    }

    public async Task AddSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default)
    {
        if (songIds.Count == 0)
        {
            return;
        }

        var current = await CacheAsync();

        // New songs go after the last one there (positions are 0-based).
        var next = Live(current, "setlistitem", ClientJsonContext.Default.SetlistItemDto)
            .Where(item => item.SetlistId == setlistId)
            .Select(item => item.Position + 1)
            .DefaultIfEmpty(0)
            .Max();

        for (var i = 0; i < songIds.Count; i++)
        {
            current.Create("setlistitem", Node(new SetlistItemDto(Guid.NewGuid(), next + i, songIds[i], setlistId, Removed: false), ClientJsonContext.Default.SetlistItemDto));
        }

        Queued(current);
    }

    public Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default)
        => UpdateGroupAsync(groupId, new JsonObject { ["currentSetlistId"] = setlistId.ToString(), ["currentSetlistItemId"] = null });

    public Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default)
        => UpdateGroupAsync(groupId, new JsonObject { ["currentSetlistId"] = entry.SetlistId.ToString(), ["currentSetlistItemId"] = entry.Id.ToString() });

    private async Task UpdateGroupAsync(Guid groupId, JsonObject fields)
    {
        var current = await CacheAsync();
        if (Find(current, "group", groupId, ClientJsonContext.Default.GroupDto) == null)
        {
            throw new InvalidOperationException("The group no longer exists.");
        }

        current.Update("group", groupId, fields);
        Queued(current);
    }

    // ---- Online only ----

    /// <summary>LRCLIB, a free online lyrics database; it allows calls from any web page (CORS).</summary>
    private static readonly HttpClient Lrclib = CreateLrclibClient();

    private static HttpClient CreateLrclibClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(20) };
        // LRCLIB asks apps to identify themselves; browsers don't let pages set the User-Agent.
        if (!OperatingSystem.IsBrowser())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Lyrious/1.0");
        }

        return client;
    }

    /// <summary>Searches LRCLIB; needs an internet connection (throws without one).</summary>
    public async Task<IReadOnlyList<InternetSong>> SearchInternetAsync(string query, CancellationToken cancellationToken = default)
    {
        using var response = await Lrclib.GetAsync($"api/search?q={Uri.EscapeDataString(query)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The lyrics database answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }

        var tracks = await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.LrclibTrackArray, cancellationToken) ?? [];
        return tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.TrackName))
            .Select(track => new InternetSong(
                track.TrackName!.Trim(),
                track.ArtistName?.Trim() ?? "",
                track.AlbumName?.Trim() ?? "",
                TimeSpan.FromSeconds(track.Duration ?? 0),
                track.PlainLyrics?.Trim() ?? "",
                track.Instrumental))
            .ToArray();
    }

    // ---- Conversions ----

    private static string Limit(string value, int max) => value.Length > max ? value[..max] : value;

    private static GroupSummary ToSummary(GroupDto group) => new(group.Id, group.Name, group.CurrentSetlistId, group.CurrentSetlistItemId);

    private static SetlistSummary ToSummary(SetlistDto setlist) => new(setlist.Id, setlist.Name, setlist.GroupId);

    private static SetlistEntry ToEntry(SetlistItemDto item, SongDto song) => new(item.Id, item.SetlistId, item.Position, ToDetails(song));

    private static SongDetails ToDetails(SongDto song) => new(song.Id, song.Name, song.Key ?? "", song.Tempo, song.Beat ?? "", song.Lyrics ?? "", song.MediaLink ?? "");
}
