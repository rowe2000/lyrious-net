using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Lyrious.CoreLib.Client;

/// <summary>
/// Connection to the API's LyriousHub. The server sends "Update{EntityType}" with changed entities
/// for the groups the member belongs to; each arrives as <see cref="EntitiesChanged"/>.
/// Messages sent while disconnected are not replayed, so <see cref="Reconnected"/> means
/// "anything may have changed".
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
	public const string HubPath = "/hubs/lyrious";

	private readonly HubConnection connection;

	public HubClient(string baseUrl, Func<string?> accessTokenProvider)
	{
		connection = new HubConnectionBuilder()
			.WithUrl(new Uri(new Uri(baseUrl), HubPath), options => options.AccessTokenProvider = () => Task.FromResult(accessTokenProvider()))
			.WithAutomaticReconnect()
			.Build();

		On<Group>();
		On<Member>();
		On<Membership>();
		On<Play>();
		On<Playlog>();
		On<Setlist>();
		On<SetlistItem>();
		On<Song>();
		On<Songbook>();

		connection.Reconnected += connectionId =>
		{
			Reconnected?.Invoke(connectionId);
			return Task.CompletedTask;
		};
	}

	/// <summary>Raised on a SignalR thread with the entity type and the changed entities.</summary>
	public event Action<Type, IEntity[]>? EntitiesChanged;

	/// <summary>Raised with the new connection id after an automatic reconnect.</summary>
	public event Action<string?>? Reconnected;

	public string? ConnectionId => connection.ConnectionId;

	public bool IsConnected => connection.State == HubConnectionState.Connected;

	public async Task StartAsync()
	{
		if (connection.State == HubConnectionState.Disconnected)
		{
			await connection.StartAsync();
		}
	}

	public Task StopAsync() => connection.StopAsync();

	/// <summary>Starts receiving a group's changes; false if the member isn't in the group.</summary>
	public Task<bool> SubscribeGroupAsync(Guid groupId) => connection.InvokeAsync<bool>("SubscribeGroup", groupId);

	public ValueTask DisposeAsync() => connection.DisposeAsync();

	private void On<TEntity>() where TEntity : class, IEntity, new()
	{
		connection.On<TEntity[]>($"Update{typeof(TEntity).Name}", entities => EntitiesChanged?.Invoke(typeof(TEntity), entities));
	}
}
