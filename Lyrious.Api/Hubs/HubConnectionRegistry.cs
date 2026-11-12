using System.Collections.Concurrent;

namespace Lyrious.Api.Hubs;

/// <summary>
/// Live hub connections per member (user id), so the server can change a member's group subscriptions
/// the moment their membership changes. In-memory: with several API instances behind a load balancer
/// this needs a shared store (or the Redis/Azure SignalR backplane's group support).
/// </summary>
public sealed class HubConnectionRegistry
{
	private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> connectionsByUser = new();

	public void Add(string userId, string connectionId)
	{
		connectionsByUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>()).TryAdd(connectionId, 0);
	}

	public void Remove(string userId, string connectionId)
	{
		if (connectionsByUser.TryGetValue(userId, out var connections))
		{
			connections.TryRemove(connectionId, out _);
		}
	}

	public IReadOnlyCollection<string> GetConnections(string userId)
	{
		return connectionsByUser.TryGetValue(userId, out var connections) ? connections.Keys.ToArray() : [];
	}
}
