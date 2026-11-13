using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.UILib.Setlists;

namespace Lyrious.Client;

/// <summary>
/// Setlist and song data from the Lyrious REST API, authenticated with the token from
/// <see cref="HttpAuthService"/>. The API only returns what the member may see.
/// </summary>
public sealed class HttpSetlistService(HttpAuthService auth) : ISetlistService
{
    private HttpClient Http => auth.Http;

    public async Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default)
    {
        var groups = await GetGroupsAsync(cancellationToken);
        return groups.FirstOrDefault(group => group.Id == auth.JoinedGroupId) ?? groups.FirstOrDefault();
    }

    public async Task<IReadOnlyList<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return (await GetAsync("api/group", ClientJsonContext.Default.GroupDtoArray, cancellationToken) ?? [])
            .Where(group => !group.Removed)
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new GroupSummary(group.Id, group.Name, group.CurrentSetlistId, group.CurrentSetlistItemId))
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>>> GetGroupMembersAsync(CancellationToken cancellationToken = default)
    {
        const int approved = 2;
        var memberships = await GetAsync("api/membership", ClientJsonContext.Default.MembershipDtoArray, cancellationToken) ?? [];
        var names = (await GetAsync("api/member", ClientJsonContext.Default.MemberDtoArray, cancellationToken) ?? [])
            .ToDictionary(member => member.Id, member => !string.IsNullOrWhiteSpace(member.Name) ? member.Name : member.UserName ?? "Unknown");

        // Admins first, then conductors, then members; each by name.
        return memberships
            .Where(membership => !membership.Removed)
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

    public async Task<GroupSummary> CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException("You're not logged in.");
        const int admin = 2, approved = 2;

        // The order matters: the API accepts the creator's membership only once the group exists,
        // and everything else in the group only from its members. Ids are chosen by the client.
        var now = DateTime.Now;
        var group = new GroupDto(Guid.NewGuid(), name, CurrentSetlistId: null, CurrentPlayId: null, CurrentSetlistItemId: null, now, now, Removed: false, Checksum: 0);
        await PostAsync("api/group", [group], ClientJsonContext.Default.GroupDtoArray, cancellationToken);

        var membership = new MembershipDto(Guid.NewGuid(), admin, approved, group.Id, memberId, Removed: false);
        await PostAsync("api/membership", [membership], ClientJsonContext.Default.MembershipDtoArray, cancellationToken);

        var songbook = new SongbookDto(Guid.NewGuid(), "Default", group.Id, Removed: false);
        await PostAsync("api/songbook", [songbook], ClientJsonContext.Default.SongbookDtoArray, cancellationToken);

        var setlist = await CreateSetlistAsync(group.Id, StartPage.DefaultSetlistName, cancellationToken);
        group = group with { CurrentSetlistId = setlist.Id };
        await PostAsync("api/group", [group], ClientJsonContext.Default.GroupDtoArray, cancellationToken);

        return new GroupSummary(group.Id, group.Name, group.CurrentSetlistId, group.CurrentSetlistItemId);
    }

    /// <summary>Joins the group by changing the member's joined group.</summary>
    public async Task SelectGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var memberId = auth.MemberId ?? throw new InvalidOperationException("You're not logged in.");
        await ChangeAsync("member", memberId, member => member["joinedGroupId"] = groupId.ToString(), cancellationToken);
        auth.JoinedGroupId = groupId;
    }

    public async Task<SongDetails> UpdateLyricsAsync(Guid songId, string lyrics, CancellationToken cancellationToken = default)
    {
        await ChangeAsync("song", songId, song => song["lyrics"] = lyrics, cancellationToken);
        var stored = await GetAsync($"api/song/{songId}", ClientJsonContext.Default.SongDto, cancellationToken)
                     ?? throw new InvalidOperationException("The song no longer exists.");
        return ToDetails(stored);
    }

    /// <summary>
    /// Reads an entity, changes it and posts it back whole, as untyped JSON: the API replaces every
    /// field with what it receives, so any field left out (or unknown to this client) would be wiped.
    /// </summary>
    private async Task ChangeAsync(string entity, Guid id, Action<JsonObject> change, CancellationToken cancellationToken)
    {
        using var get = Authorized(HttpMethod.Get, $"api/{entity}/{id}");
        using var response = await Http.SendAsync(get, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"The {entity} no longer exists.");
        }

        ThrowIfFailed(response);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonObject
                   ?? throw new InvalidOperationException("The server sent an unexpected response.");
        change(json);

        using var post = Authorized(HttpMethod.Post, $"api/{entity}");
        post.Content = new StringContent(new JsonArray(json).ToJsonString(), Encoding.UTF8, "application/json");
        using var posted = await Http.SendAsync(post, cancellationToken);
        ThrowIfFailed(posted);
    }

    public async Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return (await GetAsync("api/setlist", ClientJsonContext.Default.SetlistDtoArray, cancellationToken) ?? [])
            .Where(setlist => setlist.GroupId == groupId && !setlist.Removed)
            .OrderBy(setlist => setlist.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default)
    {
        var setlist = await GetAsync($"api/setlist/{setlistId}", ClientJsonContext.Default.SetlistDto, cancellationToken);
        return setlist is { Removed: false } ? ToSummary(setlist) : null;
    }

    public async Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default)
    {
        // Ids are chosen by the client (the API doesn't generate them).
        var now = DateTime.Now;
        var setlist = new SetlistDto(Guid.NewGuid(), name, groupId, now, now, Removed: false, Checksum: 0);
        await PostAsync("api/setlist", [setlist], ClientJsonContext.Default.SetlistDtoArray, cancellationToken);
        return ToSummary(setlist);
    }

    public async Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default)
    {
        var items = (await GetAsync("api/setlistitem", ClientJsonContext.Default.SetlistItemDtoArray, cancellationToken) ?? [])
            .Where(item => item.SetlistId == setlistId && !item.Removed)
            .ToArray();
        if (items.Length == 0)
        {
            return [];
        }

        var songs = (await GetAsync("api/song", ClientJsonContext.Default.SongDtoArray, cancellationToken) ?? [])
            .Where(song => !song.Removed)
            .ToDictionary(song => song.Id);

        return items
            .Where(item => songs.ContainsKey(item.SongId))
            .OrderBy(item => item.Position)
            .Select(item => ToEntry(item, songs[item.SongId]))
            .ToArray();
    }

    public async Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default)
    {
        var item = await GetAsync($"api/setlistitem/{setlistItemId}", ClientJsonContext.Default.SetlistItemDto, cancellationToken);
        if (item is not { Removed: false })
        {
            return null;
        }

        var song = await GetAsync($"api/song/{item.SongId}", ClientJsonContext.Default.SongDto, cancellationToken);
        return song is { Removed: false } ? ToEntry(item, song) : null;
    }

    public async Task<IReadOnlyList<SongDetails>> GetSongbookSongsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var songbookIds = (await GetAsync("api/songbook", ClientJsonContext.Default.SongbookDtoArray, cancellationToken) ?? [])
            .Where(songbook => songbook.GroupId == groupId && !songbook.Removed)
            .Select(songbook => songbook.Id)
            .ToHashSet();

        return (await GetAsync("api/song", ClientJsonContext.Default.SongDtoArray, cancellationToken) ?? [])
            .Where(song => !song.Removed && songbookIds.Contains(song.SongbookId))
            .OrderBy(song => song.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToDetails)
            .ToArray();
    }

    public async Task AddSongsAsync(Guid setlistId, IReadOnlyList<Guid> songIds, CancellationToken cancellationToken = default)
    {
        if (songIds.Count == 0)
        {
            return;
        }

        // New songs go after the last one there (positions are 0-based).
        var next = (await GetAsync("api/setlistitem", ClientJsonContext.Default.SetlistItemDtoArray, cancellationToken) ?? [])
            .Where(item => item.SetlistId == setlistId && !item.Removed)
            .Select(item => item.Position + 1)
            .DefaultIfEmpty(0)
            .Max();

        // Ids are chosen by the client (the API doesn't generate them).
        var items = songIds
            .Select((songId, index) => new SetlistItemDto(Guid.NewGuid(), next + index, songId, setlistId, Removed: false))
            .ToArray();
        await PostAsync("api/setlistitem", items, ClientJsonContext.Default.SetlistItemDtoArray, cancellationToken);
    }

    public Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default)
        => UpdateGroupAsync(groupId, group => group with { CurrentSetlistId = setlistId, CurrentSetlistItemId = null }, cancellationToken);

    public Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default)
        => UpdateGroupAsync(groupId, group => group with { CurrentSetlistId = entry.SetlistId, CurrentSetlistItemId = entry.Id }, cancellationToken);

    /// <summary>
    /// Reads the group, changes it and posts it back whole: the API replaces every field with what
    /// it receives, so a partial group would wipe the others (e.g. the name).
    /// </summary>
    private async Task UpdateGroupAsync(Guid groupId, Func<GroupDto, GroupDto> change, CancellationToken cancellationToken)
    {
        var group = await GetAsync($"api/group/{groupId}", ClientJsonContext.Default.GroupDto, cancellationToken)
                    ?? throw new InvalidOperationException("The group no longer exists.");
        await PostAsync("api/group", [change(group)], ClientJsonContext.Default.GroupDtoArray, cancellationToken);
    }

    /// <summary>GET with the bearer token; null for 404 (missing or not visible to the member).</summary>
    private async Task<T?> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, path);
        using var response = await Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        ThrowIfFailed(response);
        return await response.Content.ReadFromJsonAsync(type, cancellationToken);
    }

    private async Task PostAsync<T>(string path, T body, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body, type);
        using var response = await Http.SendAsync(request, cancellationToken);
        ThrowIfFailed(response);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(auth.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        }

        return request;
    }

    private static void ThrowIfFailed(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Log in again.",
            HttpStatusCode.Forbidden => "You're not allowed to change this.",
            _ => $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.",
        }, null, response.StatusCode);
    }

    private static SetlistSummary ToSummary(SetlistDto setlist) => new(setlist.Id, setlist.Name, setlist.GroupId);

    private static SetlistEntry ToEntry(SetlistItemDto item, SongDto song)
        => new(item.Id, item.SetlistId, item.Position, ToDetails(song));

    private static SongDetails ToDetails(SongDto song) => new(song.Id, song.Name, song.Key ?? "", song.Tempo, song.Beat ?? "", song.Lyrics ?? "");
}
