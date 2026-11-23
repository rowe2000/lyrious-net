using System;
using System.Collections.Generic;
using System.IO;
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
using Lyrious.UILib.Localization;

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
    /// <param name="instances">
    /// The app's other instances sharing the store (browser tabs on the same address), to stay the
    /// same as them without the server; none where the app is alone with its store.
    /// </param>
    public HttpSetlistService(HttpAuthService auth, ISettingsStore? store = null, IInstanceLink? instances = null)
    {
        this.auth = auth;
        this.store = store ?? new MemorySettingsStore();
        this.instances = instances;
        live = new LiveUpdates(auth);
        engine = new SyncEngine(auth, () => live.ConnectionId, () => this.instances?.IsOwner ?? true);
        engine.Completed += (change, stored) => PostToInstances("complete", change, stored);
        engine.Dropped += change => PostToInstances("drop", change);

        live.EntitiesChanged += OnServerChanged;
        live.PlaybackChanged += (groupId, playing) => PlaybackChanged?.Invoke(groupId, playing);
        live.PlaybackPositionChanged += (groupId, position) => PlaybackPositionChanged?.Invoke(groupId, position);
        live.SongSelected += OnSongSelected;
        live.MemberConnectionChanged += (memberId, connected) => MemberConnectionChanged?.Invoke(memberId, connected);
        live.SettingsChanged += settings => MySettingsChanged?.Invoke(settings);
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

    public event Action<Guid, double>? PlaybackPositionChanged;

    public event Action<Guid, bool>? MemberConnectionChanged;
    public event Action? MembersChanged;

    public event Action<string>? MySettingsChanged;

    public event Action<Guid>? MySongSettingChanged;

    public event Action? StatusChanged;

    public SyncStatus Status => new(engine.Online, cache?.PendingCount ?? 0, cache?.LastSynced, engine.Problem);

    public Task SendPlaybackAsync(bool playing) => live.SetPlaybackAsync(playing);

    public Task SendPlaybackPositionAsync(double position) => live.SetPlaybackPositionAsync(position);

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
        // Logged out (or closing): another instance takes over sending the changes.
        LeaveInstances();
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
        var memberId = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
        if (ready == null || readySession != auth.Session || cache?.MemberId != memberId)
        {
            readySession = auth.Session;
            ready = LoadAndSyncAsync(memberId);
        }
        else
        {
            // Left on logout; the same session can go on (e.g. back from another page).
            JoinInstances(memberId);
        }

        return ready;
    }

    private async Task<EntityCache> LoadAndSyncAsync(Guid memberId)
    {
        if (cache?.MemberId != memberId)
        {
            LeaveInstances();
            cache = EntityCache.Load(store, memberId);
            cache.Enqueued += OnChangeMadeHere;
            madeHere.Clear();
            StatusChanged?.Invoke();
        }

        // Before the sync: whether this instance sends the changes is known by then.
        JoinInstances(memberId);

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

    // ---- The app's other instances sharing the store (IInstanceLink) ----
    // Each has the cache in memory. A change made in one is posted to the others, which queue it
    // too, so all show it at once, with or without the server. Only the owner sends the queue to
    // the server, and posts how each change went ("complete" with the server's copy, or "drop"),
    // for the others to take it off their queues. An instance that starts asks the owner for its
    // queue ("hello", answered with "state"): the copy it loaded may be a little behind.

    private readonly IInstanceLink? instances;
    private readonly string instanceId = Guid.NewGuid().ToString("N");
    // The changes made in this instance (their keys), kept when taking over the owner's queue.
    private readonly HashSet<string> madeHere = [];
    private Guid? joinedInstancesOf;

    private void JoinInstances(Guid memberId)
    {
        if (instances == null || joinedInstancesOf == memberId)
        {
            return;
        }

        joinedInstancesOf = memberId;
        instances.Join($"cache.{memberId}", OnInstanceMessage, OnBecameOwner);
        instances.Post(new JsonObject { ["t"] = "hello", ["from"] = instanceId }.ToJsonString());
    }

    private void LeaveInstances()
    {
        if (joinedInstancesOf != null)
        {
            joinedInstancesOf = null;
            instances?.Leave();
        }
    }

    private void OnChangeMadeHere(PendingChange change)
    {
        madeHere.Add(EntityCache.KeyOf(change));
        RememberChangedHere(change.Type, change.Id);
        PostToInstances("change", change);
    }

    // ---- What changed since the last visit (see GetChangesSinceAsync) ----

    private static string ChangedHereKey(Guid memberId) => $"made-here:{memberId:N}";

    /// <summary>The entities changed on this device since the changes were last looked at ("type/id"), kept between runs.</summary>
    private static HashSet<string> ChangedHere(Guid memberId)
    {
        try
        {
            return App.Settings.Get(ChangedHereKey(memberId)) is { Length: > 0 } saved && JsonNode.Parse(saved) is JsonArray list
                ? list.Select(item => (string?)item).OfType<string>().ToHashSet()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void RememberChangedHere(string type, Guid id)
    {
        if (auth.MemberId is not { } memberId)
        {
            return;
        }

        var keys = ChangedHere(memberId);
        if (keys.Add($"{type}/{id:N}"))
        {
            App.Settings.Set(ChangedHereKey(memberId), new JsonArray(keys.Select(key => (JsonNode?)key).ToArray()).ToJsonString());
        }
    }

    public void ForgetLocalChanges()
    {
        if (auth.MemberId is { } memberId)
        {
            App.Settings.Set(ChangedHereKey(memberId), null);
        }
    }

    public async Task<IReadOnlyList<Change>> GetChangesSinceAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
        var current = await CacheAsync();
        var changedHere = ChangedHere(memberId);

        static DateTime? Time(JsonObject entity, string field)
            => entity[field] is JsonValue value && value.TryGetValue<string>(out var text) && DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var time)
                ? (time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time)
                : null;
        static Guid? IdField(JsonObject entity, string field)
            => entity[field] is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) ? id : null;
        static string Text(JsonObject? entity, string field) => entity?[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        bool Mine(string type, JsonObject entity) => changedHere.Contains($"{type}/{EntityCache.IdOf(entity):N}");

        var live = (string type) => current.All(type).Where(entity => !EntityCache.IsRemoved(entity)).ToList();
        var groups = live("group").ToDictionary(EntityCache.IdOf);
        var members = current.All("member").ToDictionary(EntityCache.IdOf);
        var songbooks = live("songbook").ToDictionary(EntityCache.IdOf);
        var setlists = live("setlist").ToDictionary(EntityCache.IdOf);
        string BandName(Guid? groupId) => groupId is { } id && groups.TryGetValue(id, out var group) ? Text(group, "name") : "";

        var changes = new List<Change>();

        foreach (var membership in live("membership").Where(m => Time(m, "createdAt") > since && !Mine("membership", m)))
        {
            var groupId = IdField(membership, "groupId");
            if (IdField(membership, "memberId") == memberId)
            {
                // Added to a band (invited, or a new one of their own made elsewhere).
                changes.Add(new Change(ChangeKind.Band, BandName(groupId), BandName(groupId), true, Time(membership, "createdAt")!.Value));
            }
            else
            {
                var name = IdField(membership, "memberId") is { } other && members.TryGetValue(other, out var member) ? Text(member, "name") : "";
                changes.Add(new Change(ChangeKind.Member, name.Length > 0 ? name : L.T("A new member"), BandName(groupId), true, Time(membership, "createdAt")!.Value));
            }
        }

        // A setlist is changed when it is, or when songs in it were added or moved.
        var changedSetlists = new Dictionary<Guid, DateTime>();
        foreach (var item in live("setlistitem").Where(item => !Mine("setlistitem", item)))
        {
            var when = new[] { Time(item, "createdAt"), Time(item, "changedAt") }.Max();
            if (when > since && IdField(item, "setlistId") is { } setlistId)
            {
                changedSetlists[setlistId] = changedSetlists.TryGetValue(setlistId, out var before) && before > when.Value ? before : when.Value;
            }
        }

        foreach (var (id, setlist) in setlists)
        {
            var band = BandName(IdField(setlist, "groupId"));
            if (Mine("setlist", setlist))
            {
                continue;
            }

            if (Time(setlist, "createdAt") is { } created && created > since)
            {
                changes.Add(new Change(ChangeKind.Setlist, Text(setlist, "name"), band, true, created));
            }
            else if (new[] { Time(setlist, "changedAt"), changedSetlists.TryGetValue(id, out var itemsChanged) ? itemsChanged : null }.Max() is { } changed && changed > since)
            {
                changes.Add(new Change(ChangeKind.Setlist, Text(setlist, "name"), band, false, changed));
            }
        }

        foreach (var song in live("song").Where(song => !Mine("song", song)))
        {
            var band = IdField(song, "songbookId") is { } bookId && songbooks.TryGetValue(bookId, out var book) ? BandName(IdField(book, "groupId")) : "";
            if (Time(song, "createdAt") is { } created && created > since)
            {
                changes.Add(new Change(ChangeKind.Song, Text(song, "name"), band, true, created));
            }
            else if (Time(song, "changedAt") is { } changed && changed > since)
            {
                changes.Add(new Change(ChangeKind.Song, Text(song, "name"), band, false, changed));
            }
        }

        return changes.OrderByDescending(change => change.When).ToList();
    }

    private void PostToInstances(string what, PendingChange change, JsonObject? stored = null)
    {
        if (instances == null || joinedInstancesOf == null)
        {
            return;
        }

        var message = new JsonObject { ["t"] = what, ["change"] = EntityCache.ToJson(change) };
        if (stored != null)
        {
            message["stored"] = stored.DeepClone();
        }

        instances.Post(message.ToJsonString());
    }

    /// <summary>The owner went away and this instance took over: send what's waiting.</summary>
    private void OnBecameOwner()
    {
        if (cache is { } current)
        {
            _ = engine.PushAsync(current);
        }
    }

    private void OnInstanceMessage(string json)
    {
        try
        {
            if (cache is not { } current || joinedInstancesOf != current.MemberId || JsonNode.Parse(json) is not JsonObject message)
            {
                return;
            }

            var change = message["change"] is JsonObject changeJson ? EntityCache.FromJson(changeJson) : null;
            switch ((string?)message["t"])
            {
                case "hello" when instances!.IsOwner:
                    var queue = new JsonArray();
                    foreach (var pending in current.Pending)
                    {
                        queue.Add((JsonNode)EntityCache.ToJson(pending));
                    }

                    instances.Post(new JsonObject { ["t"] = "state", ["to"] = (string?)message["from"], ["queue"] = queue }.ToJsonString());
                    break;
                case "state" when (string?)message["to"] == instanceId && message["queue"] is JsonArray theirs:
                    current.AdoptPending(theirs.OfType<JsonObject>().Select(EntityCache.FromJson).OfType<PendingChange>().ToList(), madeHere);
                    StatusChanged?.Invoke();
                    break;
                case "change" when change != null:
                    if (current.AddFromInstance(change))
                    {
                        ShowChangeFromInstance(current, change.Type, change.Id);
                        // The owner sends it (now if online); the others just show it.
                        _ = engine.PushAsync(current);
                    }

                    break;
                case "complete" when change != null:
                    current.CompleteFromInstance(EntityCache.KeyOf(change), change.Type, change.Id, message["stored"] as JsonObject);
                    ShowChangeFromInstance(current, change.Type, change.Id);
                    break;
                case "drop" when change != null:
                    current.DropFromInstance(EntityCache.KeyOf(change));
                    ShowChangeFromInstance(current, change.Type, change.Id);
                    break;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            // Not a message this version understands (another version of the app in the other tab).
            System.Diagnostics.Debug.WriteLine($"Ignoring a message from another instance: {ex.Message}");
        }
    }

    /// <summary>Tells the app about an entity another instance changed, as for a live update from the server.</summary>
    private void ShowChangeFromInstance(EntityCache current, string type, Guid id)
    {
        if (type == "group" && current.Find("group", id) is { } group)
        {
            GroupChanged?.Invoke(ToSummary(As(group, ClientJsonContext.Default.GroupDto)));
        }
        else if (type == "member")
        {
            FollowJoinedGroup(current);
        }
        else if (type == "songsetting" && current.Find("songsetting", id) is { } setting)
        {
            MySongSettingChanged?.Invoke(As(setting, ClientJsonContext.Default.SongSettingDto).SongId);
        }

        if (type is "member" or "membership")
        {
            MembersChanged?.Invoke();
        }

        StatusChanged?.Invoke();
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
        else if (type == "songsetting")
        {
            foreach (var setting in shown)
            {
                MySongSettingChanged?.Invoke(As(setting, ClientJsonContext.Default.SongSettingDto).SongId);
            }
        }

        if (type is "member" or "membership")
        {
            MembersChanged?.Invoke();
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
        var profiles = current.All("member")
            .Select(member => As(member, ClientJsonContext.Default.MemberDto))
            .ToDictionary(member => member.Id);
        var names = profiles.ToDictionary(entry => entry.Key, entry => !string.IsNullOrWhiteSpace(entry.Value.Name) ? entry.Value.Name : entry.Value.UserName ?? "Unknown");
        var me = auth.MemberId;
        var connected = await ConnectedMembersAsync();

        // Admins first, then conductors, then members; each by name.
        return Live(current, "membership", ClientJsonContext.Default.MembershipDto)
            .Select(membership => (membership.GroupId, Member: new GroupMember(
                membership.MemberId,
                names.GetValueOrDefault(membership.MemberId, "Unknown"),
                Enum.IsDefined(typeof(GroupRole), membership.RoleEnum) ? (GroupRole)membership.RoleEnum : GroupRole.Member,
                membership.MembershipStatusEnum == approved,
                profiles.GetValueOrDefault(membership.MemberId)?.LastLoginAt,
                membership.MemberId == me,
                profiles.GetValueOrDefault(membership.MemberId)?.JoinedGroupId == membership.GroupId,
                connected.Contains(membership.MemberId))))
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
                        songs.TryGetValue(play.SongId, out var song) ? song.Name : L.T("Unknown song"),
                        play.KeyString ?? "",
                        play.Tempo,
                        TimeSpan.FromMilliseconds(play.DurationMilliSeconds),
                        members.GetValueOrDefault(play.ConductorId, ""),
                        play.SetlistId))
                    .ToArray()))
            .ToArray();
    }

    // ---- Changing (into the cache, then sent) ----

    public async Task<Guid?> AddPlayAsync(Guid groupId, Guid songId, Guid? setlistId = null, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
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

        var newPlay = new PlayDto(Guid.NewGuid(), playlog.Id, songId, memberId, song.Key ?? "", song.Tempo, 0, now, now, Removed: false, SetlistId: setlistId);
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

    public async Task<MySongSetting> GetMySongSettingAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        if (auth.MemberId is not { } memberId)
        {
            return MySongSetting.None;
        }

        var current = await CacheAsync();
        return MySetting(current, memberId, songId) is { } setting
            ? new MySongSetting(Math.Clamp(setting.Transpose, MySongSetting.MinTranspose, MySongSetting.MaxTranspose), setting.Lyrics ?? "", setting.TransposeOff, setting.MidiSound ?? "")
            : MySongSetting.None;
    }

    public async Task SaveMySongSettingAsync(Guid songId, MySongSetting setting, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
        var transpose = Math.Clamp(setting.Transpose, MySongSetting.MinTranspose, MySongSetting.MaxTranspose);
        var lyrics = setting.Lyrics ?? "";
        var midiSound = setting.MidiSound ?? "";
        var current = await CacheAsync();
        if (MySetting(current, memberId, songId) is { } stored)
        {
            if (stored.Transpose == transpose && (stored.Lyrics ?? "") == lyrics && stored.TransposeOff == setting.TransposeOff && (stored.MidiSound ?? "") == midiSound)
            {
                return;
            }

            var fields = new JsonObject { ["transpose"] = transpose, ["lyrics"] = lyrics, ["transposeOff"] = setting.TransposeOff };
            // Only when it changes: a server from before it ignores it, and then the rest still goes.
            if ((stored.MidiSound ?? "") != midiSound)
            {
                fields["midiSound"] = midiSound;
            }

            current.Update("songsetting", stored.Id, fields);
        }
        else
        {
            var now = DateTime.Now;
            current.Create("songsetting", Node(new SongSettingDto(Guid.NewGuid(), memberId, songId, transpose, lyrics, now, now, Removed: false, TransposeOff: setting.TransposeOff, MidiSound: midiSound), ClientJsonContext.Default.SongSettingDto));
        }

        Queued(current);
    }

    public async Task<IReadOnlyList<MemberLyrics>> GetMembersLyricsAsync(Guid groupId, Guid songId, CancellationToken cancellationToken = default)
    {
        if (auth.MemberId is not { } me)
        {
            return [];
        }

        var members = (await GetGroupMembersAsync(cancellationToken)).GetValueOrDefault(groupId) ?? [];
        if (!members.Any(member => member.IsMe && member.Role == GroupRole.Admin))
        {
            return [];
        }

        var current = await CacheAsync();
        return members
            .Where(member => !member.IsMe && member.Approved)
            .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(member => new MemberLyrics(member.MemberId, member.Name, MySetting(current, member.MemberId, songId)?.Lyrics ?? ""))
            .ToArray();
    }

    public async Task SaveMemberLyricsAsync(Guid songId, Guid memberId, string lyrics, CancellationToken cancellationToken = default)
    {
        _ = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
        var current = await CacheAsync();
        if (MySetting(current, memberId, songId) is { } stored)
        {
            if ((stored.Lyrics ?? "") == lyrics)
            {
                return;
            }

            // Only the lyrics: their transpose stays theirs.
            current.Update("songsetting", stored.Id, new JsonObject { ["lyrics"] = lyrics });
        }
        else if (lyrics.Length > 0)
        {
            var now = DateTime.Now;
            current.Create("songsetting", Node(new SongSettingDto(Guid.NewGuid(), memberId, songId, 0, lyrics, now, now, Removed: false), ClientJsonContext.Default.SongSettingDto));
        }
        else
        {
            return;
        }

        Queued(current);
    }

    /// <summary>The member's own settings for the song (the latest, should two devices have made one each offline).</summary>
    private static SongSettingDto? MySetting(EntityCache current, Guid memberId, Guid songId) =>
        Live(current, "songsetting", ClientJsonContext.Default.SongSettingDto)
            .Where(o => o.MemberId == memberId && o.SongId == songId)
            .MaxBy(o => o.ChangedAt);

    private static JsonObject Node<T>(T value, JsonTypeInfo<T> type) => (JsonObject)JsonSerializer.SerializeToNode(value, type)!;

    public async Task<GroupSummary> CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
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
        var memberId = auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in."));
        var current = await CacheAsync();
        current.Update("member", memberId, new JsonObject { ["joinedGroupId"] = groupId.ToString() });

        // Joining a band one is invited to accepts the invitation.
        const int approved = 2;
        if (MembershipOf(current, groupId, memberId) is { } invited && invited.MembershipStatusEnum != approved)
        {
            current.Update("membership", invited.Id, new JsonObject { ["membershipStatusEnum"] = approved });
        }

        auth.SetJoinedGroup(groupId);
        Queued(current);
    }

    /// <summary>Who's connected right now; none when offline (or if the server can't say).</summary>
    private async Task<HashSet<Guid>> ConnectedMembersAsync()
    {
        try
        {
            return [.. await live.GetConnectedMembersAsync().WaitAsync(TimeSpan.FromSeconds(3))];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't get who's connected: {ex.Message}");
            return [];
        }
    }

    private static MembershipDto? MembershipOf(EntityCache cache, Guid groupId, Guid memberId)
        => Live(cache, "membership", ClientJsonContext.Default.MembershipDto).FirstOrDefault(m => m.GroupId == groupId && m.MemberId == memberId);

    public async Task<FoundMember?> FindMemberAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("Finding a member to invite needs a connection to the server."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/member/find?query={Uri.EscapeDataString(query.Trim())}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var response = await auth.Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var member = await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.MemberDto, cancellationToken);
        return member == null ? null : new FoundMember(member.Id, !string.IsNullOrWhiteSpace(member.Name) ? member.Name : member.UserName ?? "", member.UserName ?? "");
    }

    public async Task InviteMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken = default)
    {
        const int member = 0, invited = 0;
        var current = await CacheAsync();
        if (MembershipOf(current, groupId, memberId) != null)
        {
            throw new InvalidOperationException(L.T("They're in the band already."));
        }

        current.Create("membership", Node(new MembershipDto(Guid.NewGuid(), member, invited, groupId, memberId, Removed: false), ClientJsonContext.Default.MembershipDto));
        Queued(current);
    }

    public async Task InviteByEmailAsync(Guid groupId, string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("Inviting by email needs a connection to the server."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/member/invite-email");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Content = JsonContent.Create(new EmailInvitationRequest(groupId, email.Trim()), ClientJsonContext.Default.EmailInvitationRequest);
        using var response = await auth.Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await HttpAuthService.ReadErrorAsync(response, cancellationToken));
        }
    }

    public async Task UploadSoundAsync(Guid songId, string soundId, Stream content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("Sending a sound file needs a connection to the server."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/sound/{Uri.EscapeDataString(soundId)}?songId={songId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await auth.Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await HttpAuthService.ReadErrorAsync(response, cancellationToken));
        }
    }

    public async Task<Stream?> DownloadSoundAsync(string soundId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("Fetching a sound file needs a connection to the server."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/sound/{Uri.EscapeDataString(soundId)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await auth.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw new InvalidOperationException(await HttpAuthService.ReadErrorAsync(response, cancellationToken));
            }
        }

        return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    /// <summary>A response's content as it comes in; disposing it lets go of the response.</summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream content) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => response.Content.Headers.ContentLength ?? throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => content.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => content.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => content.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public async Task SetMemberRoleAsync(Guid groupId, Guid memberId, GroupRole role, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        var membership = MembershipOf(current, groupId, memberId) ?? throw new InvalidOperationException(L.T("They're no longer in the band."));
        current.Update("membership", membership.Id, new JsonObject { ["roleEnum"] = (int)role });
        Queued(current);
    }

    public async Task RenameMemberAsync(Guid memberId, string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException(L.T("A name is needed."));
        }

        var current = await CacheAsync();
        current.Update("member", memberId, new JsonObject { ["name"] = name });
        Queued(current);
    }

    public Task RenameMeAsync(string name, CancellationToken cancellationToken = default)
        => RenameMemberAsync(auth.MemberId ?? throw new InvalidOperationException(L.T("You're not logged in.")), name, cancellationToken);

    public async Task<string?> GetMySettingsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendMySettingsAsync(HttpMethod.Get, null, cancellationToken);
        var settings = await response.Content.ReadAsStringAsync(cancellationToken);
        // 204 No Content: never saved.
        return string.IsNullOrWhiteSpace(settings) ? null : settings;
    }

    public async Task SaveMySettingsAsync(string settings, CancellationToken cancellationToken = default)
    {
        using var response = await SendMySettingsAsync(HttpMethod.Put, settings, cancellationToken);
    }

    public async Task<IReadOnlyList<ReleaseNoteEntry>> GetReleaseNotesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("The release notes need a connection to the server."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/releasenote");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var response = await auth.Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var notes = await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.ReleaseNoteDtoArray, cancellationToken) ?? [];
        return notes
            .Where(note => !note.Removed)
            .Select(note => new ReleaseNoteEntry(note.Id, note.Text ?? "", note.Branch ?? "", note.Version ?? "", note.Published, note.Created))
            .OrderByDescending(note => note.Created)
            .ToArray();
    }

    public async Task SaveReleaseNoteAsync(ReleaseNoteEntry note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("The release notes need a connection to the server."));
        }

        // Posted whole (added, or replacing the stored one); the server lets only site managers.
        var body = new[] { new ReleaseNoteSaveDto(note.Id, note.Text, note.Branch, note.Version, note.Published, note.Created, note.Created, DateTime.Now, Removed: false) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/releasenote")
        {
            Content = JsonContent.Create(body, ClientJsonContext.Default.ReleaseNoteSaveDtoArray),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var response = await auth.Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await HttpAuthService.ReadErrorAsync(response, cancellationToken));
        }
    }

    public async Task<IReadOnlyList<SiteMember>> GetAllMembersAsync(CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        return current.All("member")
            .Select(member => As(member, ClientJsonContext.Default.MemberDto))
            .Select(member => new SiteMember(member.Id, !string.IsNullOrWhiteSpace(member.Name) ? member.Name : member.UserName ?? "Unknown", member.UserName ?? "", member.LastLoginAt))
            .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlySet<Guid>> GetDisabledMembersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendMySettingsAsync(HttpMethod.Get, null, cancellationToken, "api/member/disabled");
        var ids = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonArray ?? [];
        return ids.Select(id => Guid.TryParse((string?)id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    public async Task SetMemberDisabledAsync(Guid memberId, bool disabled, CancellationToken cancellationToken = default)
    {
        using var response = await SendMySettingsAsync(HttpMethod.Put, disabled ? "true" : "false", cancellationToken, $"api/member/{memberId}/disabled");
    }

    public async Task<string?> GetMemberSettingsAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        using var response = await SendMySettingsAsync(HttpMethod.Get, null, cancellationToken, $"api/member/{memberId}/settings");
        var settings = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings) ? null : settings;
    }

    public async Task SaveMemberSettingsAsync(Guid memberId, string settings, CancellationToken cancellationToken = default)
    {
        using var response = await SendMySettingsAsync(HttpMethod.Put, settings, cancellationToken, $"api/member/{memberId}/settings");
    }

    /// <summary>
    /// Gets or puts api/member/me/settings. Tells the server this app's live connection, so it
    /// doesn't send this app its own change back. Throws if the server can't be reached or refuses.
    /// </summary>
    private async Task<HttpResponseMessage> SendMySettingsAsync(HttpMethod method, string? body, CancellationToken cancellationToken, string path = "api/member/me/settings")
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            throw new InvalidOperationException(L.T("Your settings need a connection to the server."));
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        if (live.ConnectionId is { } connectionId)
        {
            request.Headers.Add("X-Lyrious-ConnectionId", connectionId);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await auth.Http.SendAsync(request, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        if (MembershipOf(current, groupId, memberId) is { } membership)
        {
            current.Delete("membership", membership.Id);
            Queued(current);
        }
    }

    public async Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Delete("group", groupId);
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

    /// <summary>Gives the band a new name (not empty).</summary>
    public async Task RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException(L.T("A name is needed."));
        }

        var current = await CacheAsync();
        current.Update("group", groupId, new JsonObject { ["name"] = name });
        Queued(current);
    }

    public async Task SetMidiExpressionsAsync(Guid groupId, IReadOnlyList<MidiExpression> expressions, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Update("group", groupId, new JsonObject { ["midiExpressions"] = expressions.Count == 0 ? null : Midi.ExpressionsToJson(expressions) });
        Queued(current);
    }

    public async Task SetNextSongOnStopAtEndAsync(Guid groupId, bool? goOn, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Update("group", groupId, new JsonObject { ["nextSongOnStopAtEnd"] = goOn });
        Queued(current);
    }

    public async Task SetLyricsHeaderAsync(Guid groupId, string? header, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Update("group", groupId, new JsonObject { ["lyricsHeader"] = header });
        Queued(current);
    }

    public async Task SetMidiSongCommandsAsync(Guid groupId, MidiSongCommands commands, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        current.Update("group", groupId, new JsonObject { ["midiCommands"] = Midi.SongCommandsToJson(commands) });
        Queued(current);
    }

    /// <summary>Gives the setlist a new name (not empty).</summary>
    public async Task RenameSetlistAsync(Guid setlistId, string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException(L.T("A name is needed."));
        }

        var current = await CacheAsync();
        current.Update("setlist", setlistId, new JsonObject { ["name"] = name });
        Queued(current);
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
                       ?? throw new InvalidOperationException(L.T("The band has no songbook."));

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
        return new SongDetails(song.Id, song.Name, song.Key, song.Tempo, song.Beat, song.Lyrics, song.MediaLink, song.Tags, song.OriginalKey, song.Artist);
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
        // The server's limits: name and artist 100 characters, keys and beat 10, lyrics 5000, media link and tags 500.
        return await UpdateSongFieldsAsync(song.Id, new JsonObject
        {
            ["name"] = Limit(song.Name, 100),
            ["key"] = Limit(song.PlayingKey, 10),
            ["originalKey"] = Limit(song.OriginalKey, 10),
            ["artist"] = Limit(song.Artist.Trim(), 100),
            ["tempo"] = song.Tempo,
            ["beat"] = Limit(song.Beat, 10),
            ["lyrics"] = Limit(song.Lyrics, 5000),
            ["mediaLink"] = Limit(song.MediaLink, 500),
            ["tags"] = Limit(SongTags.Tidy(song.Tags), 500),
        });
    }

    public Task<SongDetails> UpdateLyricsAsync(Guid songId, string lyrics, CancellationToken cancellationToken = default)
        => UpdateSongFieldsAsync(songId, new JsonObject { ["lyrics"] = Limit(lyrics, 5000) });

    private async Task<SongDetails> UpdateSongFieldsAsync(Guid songId, JsonObject fields)
    {
        var current = await CacheAsync();
        if (Find(current, "song", songId, ClientJsonContext.Default.SongDto) == null)
        {
            throw new InvalidOperationException(L.T("The song no longer exists."));
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

    /// <summary>
    /// Makes the setlist's songs these, in this order: its entries are removed (the API keeps them,
    /// marked as removed) and the songs added at positions 0, 1, 2, … The setlist itself stays.
    /// </summary>
    public async Task ReplaceSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default)
    {
        var current = await CacheAsync();
        foreach (var item in Live(current, "setlistitem", ClientJsonContext.Default.SetlistItemDto).Where(item => item.SetlistId == setlistId).ToArray())
        {
            current.Delete("setlistitem", item.Id);
        }

        for (var i = 0; i < songIds.Count; i++)
        {
            current.Create("setlistitem", Node(new SetlistItemDto(Guid.NewGuid(), i, songIds[i], setlistId, Removed: false), ClientJsonContext.Default.SetlistItemDto));
        }

        Queued(current);
    }

    public Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default)
        => SelectAsync(groupId, setlistId, null);

    public Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default)
        => SelectAsync(groupId, entry.SetlistId, entry.Id);

    /// <summary>
    /// The band's current setlist and song: the other members are told at once over the live
    /// connection, then it's stored (through the outbox, like any change).
    /// </summary>
    private Task SelectAsync(Guid groupId, Guid setlistId, Guid? setlistItemId)
    {
        _ = AnnounceAsync(groupId, setlistId, setlistItemId);
        return UpdateGroupAsync(groupId, SelectionFields(setlistId, setlistItemId));
    }

    private async Task AnnounceAsync(Guid groupId, Guid? setlistId, Guid? setlistItemId)
    {
        try
        {
            await live.SelectSongAsync(groupId, setlistId, setlistItemId);
        }
        catch (Exception ex)
        {
            // The stored change still gets there, just a little later.
            System.Diagnostics.Debug.WriteLine($"Couldn't announce the current song: {ex.Message}");
        }
    }

    private static JsonObject SelectionFields(Guid? setlistId, Guid? setlistItemId) => new()
    {
        ["currentSetlistId"] = setlistId?.ToString(),
        ["currentSetlistItemId"] = setlistItemId?.ToString(),
    };

    /// <summary>Another member picked a band's current song: shown at once, before the stored group arrives.</summary>
    private void OnSongSelected(Guid groupId, Guid? setlistId, Guid? setlistItemId)
    {
        if (cache is not { } current || current.MergeFields("group", groupId, SelectionFields(setlistId, setlistItemId)) is not { } group)
        {
            return;
        }

        GroupChanged?.Invoke(ToSummary(As(group, ClientJsonContext.Default.GroupDto)));
    }

    private async Task UpdateGroupAsync(Guid groupId, JsonObject fields)
    {
        var current = await CacheAsync();
        if (Find(current, "group", groupId, ClientJsonContext.Default.GroupDto) == null)
        {
            throw new InvalidOperationException(L.T("The group no longer exists."));
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
            throw new HttpRequestException(L.T("The lyrics database answered {0} {1}.", (int)response.StatusCode, response.ReasonPhrase), null, response.StatusCode);
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

    private static GroupSummary ToSummary(GroupDto group) => new(group.Id, group.Name, group.CurrentSetlistId, group.CurrentSetlistItemId, group.IsPrivate, group.MidiExpressions, group.MidiCommands, group.LyricsHeader, group.NextSongOnStopAtEnd);

    private static SetlistSummary ToSummary(SetlistDto setlist) => new(setlist.Id, setlist.Name, setlist.GroupId);

    private static SetlistEntry ToEntry(SetlistItemDto item, SongDto song) => new(item.Id, item.SetlistId, item.Position, ToDetails(song));

    private static SongDetails ToDetails(SongDto song) => new(song.Id, song.Name, song.Key ?? "", song.Tempo, song.Beat ?? "", song.Lyrics ?? "", song.MediaLink ?? "", song.Tags ?? "", song.OriginalKey ?? "", song.Artist ?? "");
}
