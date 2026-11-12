using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        var groups = (await GetAsync("api/group", ClientJsonContext.Default.GroupDtoArray, cancellationToken) ?? [])
            .Where(group => !group.Removed)
            .ToArray();

        var group = groups.FirstOrDefault(g => g.Id == auth.JoinedGroupId)
                    ?? groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
        return group == null ? null : new GroupSummary(group.Id, group.Name, group.CurrentSetlistId, group.CurrentSetlistItemId);
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
        => new(item.Id, item.SetlistId, item.Position, new SongDetails(song.Id, song.Name, song.Key ?? "", song.Tempo, song.Beat ?? "", song.Lyrics ?? ""));
}
