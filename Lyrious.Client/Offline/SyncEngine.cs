using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.Client.Offline;

/// <summary>
/// Keeps the <see cref="EntityCache"/> and the server in step.
/// <list type="bullet">
/// <item>Upstream: the outbox is sent in order. A new entity is posted whole; a change is applied to
/// the server's latest copy of the entity (only the fields changed here), so changes other members
/// made to other fields meanwhile are kept; a delete is a delete.</item>
/// <item>Downstream: all the member's data is fetched and replaces the cache's copy, with the changes
/// not yet sent laid on top.</item>
/// </list>
/// While the server can't be reached the engine is offline and tries again every
/// <see cref="RetryInterval"/>; back online, it logs in again if needed and syncs.
/// </summary>
internal sealed class SyncEngine
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    private readonly HttpAuthService auth;
    private readonly Func<string?> connectionId;
    private readonly SemaphoreSlim running = new(1, 1);
    private CancellationTokenSource? retry;

    public SyncEngine(HttpAuthService auth, Func<string?> connectionId)
    {
        this.auth = auth;
        this.connectionId = connectionId;
    }

    /// <summary>Whether the server could be reached last time it was tried.</summary>
    public bool Online { get; private set; } = true;

    /// <summary>The last problem worth telling the member (a change the server refused, say); null if none.</summary>
    public string? Problem { get; private set; }

    /// <summary>Raised (on any thread) when <see cref="Online"/>, <see cref="Problem"/> or the cache's sync state changes.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised after a downstream sync replaced the cache's copy, with the cache's view of the groups before it.</summary>
    public event Action<IReadOnlyDictionary<Guid, JsonObject>>? Downloaded;

    /// <summary>Raised after coming back online (logged in again and synced): e.g. to reconnect live updates.</summary>
    public event Action? BackOnline;

    /// <summary>
    /// Sends the outbox, then fetches everything. Returns whether it all worked; when the server
    /// can't be reached the engine goes offline (and keeps trying) instead of throwing.
    /// </summary>
    public async Task<bool> SyncAsync(EntityCache cache)
    {
        await running.WaitAsync();
        try
        {
            var wasOnline = Online;
            // Make sure it's the Lyrious server answering before sending anything: something else on
            // the address could answer every change with an error, and they'd be dropped as refused.
            if (!await auth.IsServerUpAsync())
            {
                SetOffline(cache);
                return false;
            }

            if (!await EnsureTokenAsync())
            {
                return false;
            }

            if (!await PushAllAsync(cache))
            {
                return false;
            }

            await DownloadAsync(cache);
            SetOnline();
            if (!wasOnline)
            {
                BackOnline?.Invoke();
            }

            return true;
        }
        catch (Exception ex) when (HttpAuthService.IsNetworkFailure(ex))
        {
            SetOffline(cache);
            return false;
        }
        catch (HttpRequestException ex)
        {
            // The server answered with an error (e.g. 500): keep the changes, say so, try again later.
            SetProblem($"Couldn't sync. {ex.Message}");
            return false;
        }
        finally
        {
            running.Release();
        }
    }

    /// <summary>Sends the outbox now, if online (after a change made here). Never throws.</summary>
    public async Task PushAsync(EntityCache cache)
    {
        StatusChanged?.Invoke();
        if (!Online)
        {
            return;
        }

        await running.WaitAsync();
        try
        {
            if (!await auth.IsServerUpAsync())
            {
                SetOffline(cache);
            }
            else if (await EnsureTokenAsync())
            {
                await PushAllAsync(cache);
            }
        }
        catch (Exception ex) when (HttpAuthService.IsNetworkFailure(ex))
        {
            SetOffline(cache);
        }
        catch (HttpRequestException ex)
        {
            SetProblem($"Couldn't sync. {ex.Message}");
        }
        finally
        {
            running.Release();
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Stops trying to reach the server (on logout).</summary>
    public void Stop()
    {
        retry?.Cancel();
        retry = null;
    }

    /// <summary>The server has gone away (e.g. live updates lost): offline until it answers again.</summary>
    public void ReportOffline(EntityCache cache) => SetOffline(cache);

    /// <summary>An offline session has no token; an expired one is refused. Either way, log in again.</summary>
    private async Task<bool> EnsureTokenAsync()
    {
        if (!string.IsNullOrEmpty(auth.AccessToken))
        {
            return true;
        }

        if (await auth.ReauthenticateAsync())
        {
            return true;
        }

        SetProblem(auth.ReauthenticationProblem is { } problem && !problem.StartsWith("Wrong username", StringComparison.Ordinal)
            ? problem
            : "The server refused your login (was your password changed?). Log out and in again to sync.");
        return false;
    }

    /// <summary>Sends the outbox in order; false when it had to stop (changes remain queued).</summary>
    private async Task<bool> PushAllAsync(EntityCache cache)
    {
        while (cache.Peek() is { } change)
        {
            var outcome = await PushOneAsync(change);
            if (outcome is Outcome.NoLogin && await auth.ReauthenticateAsync())
            {
                outcome = await PushOneAsync(change);
            }

            switch (outcome)
            {
                case Outcome.Ok:
                    cache.Complete(change, await TryGetAsync(change.Type, change.Id));
                    break;
                case Outcome.Rejected rejected:
                    cache.Drop(change);
                    SetProblem(rejected.Message);
                    break;
                case Outcome.NoLogin:
                    SetProblem("The server refused your login (was your password changed?). Log out and in again to sync.");
                    return false;
            }

            StatusChanged?.Invoke();
        }

        return true;
    }

    private abstract record Outcome
    {
        public sealed record Ok : Outcome;

        public sealed record Rejected(string Message) : Outcome;

        public sealed record NoLogin : Outcome;

        public static readonly Outcome Done = new Ok();
        public static readonly Outcome Unauthorized = new NoLogin();

        public static Outcome Refused(string message) => new Rejected(message);
    }

    /// <summary>Sends one change. Throws when the server can't be reached.</summary>
    private async Task<Outcome> PushOneAsync(PendingChange change)
    {
        var path = $"api/{change.Type}";
        if (change.Kind == "delete")
        {
            using var response = await SendAsync(HttpMethod.Delete, $"{path}/{change.Id}");
            // Already gone (or not visible any more) is as good as deleted.
            return response.StatusCode == HttpStatusCode.NotFound ? Outcome.Done : Judge(response, change);
        }

        JsonObject body;
        if (change.Kind == "create")
        {
            body = change.Fields!;
        }
        else
        {
            // Apply just the fields changed here to the server's latest copy: the API replaces every
            // field with what it receives, so the rest must be the server's.
            using var get = await SendAsync(HttpMethod.Get, $"{path}/{change.Id}");
            if (get.StatusCode == HttpStatusCode.NotFound)
            {
                return Outcome.Refused($"A {Describe(change.Type)} you changed offline was deleted meanwhile, so the change was dropped.");
            }

            if (Judge(get, change) is not Outcome.Ok and var refusal)
            {
                return refusal;
            }

            body = JsonNode.Parse(await get.Content.ReadAsStringAsync()) as JsonObject
                   ?? throw new HttpRequestException("The server sent an unexpected response.");
            foreach (var (field, value) in change.Fields!)
            {
                body[field] = value?.DeepClone();
            }
        }

        using var post = await SendAsync(HttpMethod.Post, path, new JsonArray(body.DeepClone()));
        return Judge(post, change);
    }

    private static Outcome Judge(HttpResponseMessage response, PendingChange change)
    {
        if (response.IsSuccessStatusCode)
        {
            return Outcome.Done;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => Outcome.Unauthorized,
            HttpStatusCode.Forbidden => Outcome.Refused($"The server didn't allow a change to a {Describe(change.Type)} made offline (you may have left the band), so it was dropped."),
            >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                => Outcome.Refused($"The server refused a change to a {Describe(change.Type)} ({(int)response.StatusCode} {response.ReasonPhrase}), so it was dropped."),
            // 5xx: the server has a problem; keep the change and try again later.
            _ => throw new HttpRequestException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode),
        };
    }

    /// <summary>The server's stored copy after a write, for the cache; null if it can't be had right now.</summary>
    private async Task<JsonObject?> TryGetAsync(string type, Guid id)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, $"api/{type}/{id}");
            return response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject : null;
        }
        catch (Exception ex) when (HttpAuthService.IsNetworkFailure(ex))
        {
            return null;
        }
    }

    /// <summary>Fetches all the member's data and replaces the cache's copy.</summary>
    private async Task DownloadAsync(EntityCache cache)
    {
        var fetched = new Dictionary<string, List<JsonObject>>();
        foreach (var type in EntityCache.Types)
        {
            using var response = await SendAsync(HttpMethod.Get, $"api/{type}");
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
            }

            var list = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonArray ?? [];
            fetched[type] = list.OfType<JsonObject>().ToList();
        }

        var groupsBefore = cache.All("group").ToDictionary(EntityCache.IdOf, group => (JsonObject)group.DeepClone());
        cache.ReplaceSnapshot(fetched);
        Downloaded?.Invoke(groupsBefore);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(auth.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        }

        // So the server's live update for this write isn't sent back to this app.
        if (connectionId() is { } id)
        {
            request.Headers.Add("X-Lyrious-ConnectionId", id);
        }

        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return await auth.Http.SendAsync(request);
    }

    private void SetOnline()
    {
        retry?.Cancel();
        retry = null;
        if (!Online)
        {
            Online = true;
        }

        StatusChanged?.Invoke();
    }

    private void SetOffline(EntityCache cache)
    {
        Online = false;
        StatusChanged?.Invoke();
        if (retry != null)
        {
            return;
        }

        retry = new CancellationTokenSource();
        _ = RetryAsync(cache, retry.Token);
    }

    private void SetProblem(string message)
    {
        Problem = message;
        StatusChanged?.Invoke();
    }

    /// <summary>While offline: asks the server for a sign of life now and then, and syncs when it answers.</summary>
    private async Task RetryAsync(EntityCache cache, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RetryInterval, cancellationToken);
                if (!await auth.IsServerUpAsync(cancellationToken))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (HttpAuthService.IsNetworkFailure(ex))
            {
                continue;
            }

            // The server answers: stop this loop and sync (which starts a new loop if it fails again).
            if (retry?.Token == cancellationToken)
            {
                retry = null;
            }

            await SyncAsync(cache);
            return;
        }
    }

    private static string Describe(string type) => type switch
    {
        "setlistitem" => "setlist entry",
        "songbook" => "songbook",
        "membership" => "membership",
        "group" => "band",
        _ => type,
    };
}
