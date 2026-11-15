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

	/// <summary>Adds a connection; returns whether it's the member's first (they've just come online).</summary>
	public bool Add(string userId, string connectionId)
	{
		var connections = connectionsByUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
		lock (connections)
		{
			connections.TryAdd(connectionId, 0);
			return connections.Count == 1;
		}
	}

	/// <summary>Removes a connection; returns whether it was the member's last (they've gone offline).</summary>
	public bool Remove(string userId, string connectionId)
	{
		if (!connectionsByUser.TryGetValue(userId, out var connections))
		{
			return false;
		}

		lock (connections)
		{
			return connections.TryRemove(connectionId, out _) && connections.IsEmpty;
		}
	}

	/// <summary>Whether the member has a live connection (the app open and connected).</summary>
	public bool IsConnected(string userId) => connectionsByUser.TryGetValue(userId, out var connections) && !connections.IsEmpty;

	public IReadOnlyCollection<string> GetConnections(string userId)
	{
		return connectionsByUser.TryGetValue(userId, out var connections) ? connections.Keys.ToArray() : [];
	}
}
