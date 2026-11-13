using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrious.Client;

/// <summary>
/// A connection to the API's hub (/hubs/lyrious), which pushes "Update{EntityType}" with the
/// stored entities after every write, to all connected members of the band (group) that owns them,
/// and play/stop to the members that have joined the band (their <c>JoinedGroupId</c>). Messages
/// sent while disconnected aren't replayed, so a reconnect means "anything may have changed".
/// </summary>
internal sealed class LiveUpdates(HttpAuthService auth) : IAsyncDisposable
{
    private const string HubPath = "hubs/lyrious";

    /// <summary>The hub's entity type names (as in "Update{Name}") and the cache's names for them.</summary>
    private static readonly (string Hub, string Cache)[] EntityTypes =
    [
        ("Group", "group"), ("Member", "member"), ("Membership", "membership"), ("Songbook", "songbook"),
        ("Song", "song"), ("Setlist", "setlist"), ("SetlistItem", "setlistitem"),
    ];

    private HubConnection? connection;

    /// <summary>
    /// This connection's id while connected. Sent with every write (header
    /// <c>X-Lyrious-ConnectionId</c>), so the server doesn't send this app its own changes back.
    /// </summary>
    public string? ConnectionId => connection?.State == HubConnectionState.Connected ? connection.ConnectionId : null;

    public bool IsConnected => connection?.State == HubConnectionState.Connected;

    /// <summary>Raised on a background thread with the cache's type name ("song", ...) and the stored entities.</summary>
    public event Action<string, JsonObject[]>? EntitiesChanged;

    /// <summary>Raised on a background thread when another member of the joined band starts (true) or stops playing.</summary>
    public event Action<Guid, bool>? PlaybackChanged;

    /// <summary>Raised after reconnecting by itself: changes may have been missed meanwhile.</summary>
    public event Action? Reconnected;

    /// <summary>Raised when the connection is lost for good (reconnecting gave up).</summary>
    public event Action? Lost;

    /// <summary>Connects with the current login's token (again, if it was stopped). Throws if the hub can't be reached.</summary>
    public async Task StartAsync()
    {
        if (connection is { State: not HubConnectionState.Disconnected })
        {
            return;
        }

        if (connection == null)
        {
            var url = new Uri(auth.Http.BaseAddress!, HubPath);
            connection = new HubConnectionBuilder()
                .WithUrl(url, options => options.AccessTokenProvider = () => Task.FromResult(auth.AccessToken))
                // Source-generated JSON, like the REST calls, so it keeps working in trimmed builds.
                .AddJsonProtocol(options => options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ClientJsonContext.Default))
                .WithAutomaticReconnect()
                .Build();

            foreach (var (hubName, cacheName) in EntityTypes)
            {
                connection.On<JsonElement[]>($"Update{hubName}", entities =>
                    EntitiesChanged?.Invoke(cacheName, entities.Select(entity => JsonNode.Parse(entity.GetRawText())).OfType<JsonObject>().ToArray()));
            }

            connection.On<Guid, bool>("PlaybackChanged", (groupId, playing) => PlaybackChanged?.Invoke(groupId, playing));
            connection.Reconnected += _ =>
            {
                Reconnected?.Invoke();
                return Task.CompletedTask;
            };
            connection.Closed += _ =>
            {
                Lost?.Invoke();
                return Task.CompletedTask;
            };
        }

        await connection.StartAsync();
    }

    /// <summary>Tells the other members of the joined band that this member started or stopped playing (if connected).</summary>
    public async Task SetPlaybackAsync(bool playing)
    {
        if (connection is { State: HubConnectionState.Connected })
        {
            await connection.InvokeAsync<bool>("SetPlayback", playing);
        }
    }

    /// <summary>Disconnects, e.g. on logout. The next start connects afresh (perhaps to another host).</summary>
    public async Task StopAsync()
    {
        if (connection != null)
        {
            var old = connection;
            connection = null;
            await old.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection != null)
        {
            await connection.DisposeAsync();
        }
    }
}
