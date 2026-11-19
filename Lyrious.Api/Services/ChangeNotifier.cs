using Lyrious.Api.Hubs;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.SignalR;

namespace Lyrious.Api.Services;

/// <summary>Sends stored entity changes to the hub groups allowed to see them.</summary>
public sealed class ChangeNotifier(IHubContext<LyriousHub> hub, LyriousRepository repo, HubConnectionRegistry registry, ILogger<ChangeNotifier> logger)
{
	/// <summary>Request header with the caller's own hub connection id, so it isn't sent its own change back.</summary>
	public const string ConnectionIdHeader = "X-Lyrious-ConnectionId";

	public async Task NotifyAsync<TEntity>(IEnumerable<TEntity> storedEntities, string? excludeConnectionId)
		where TEntity : class, IEntity, new()
	{
		try
		{
			var method = LyriousHub.UpdateMethodName<TEntity>();
			IReadOnlyList<string> excluded = string.IsNullOrEmpty(excludeConnectionId) ? [] : [excludeConnectionId];

			var byGroup = new Dictionary<Guid, List<TEntity>>();
			foreach (var entity in storedEntities)
			{
				var payload = ToPayload(entity);
				if (entity is SongSetting setting)
				{
					// A member's own song settings go to their own devices and the band's admins only, never to the band.
					var audience = repo.SongSettingAudience(setting).Select(id => id.ToString()).ToArray();
					await hub.Clients.Users(audience).SendAsync(method, new[] { payload });
					continue;
				}

				foreach (var groupId in repo.GroupIdsOf(entity))
				{
					if (!byGroup.TryGetValue(groupId, out var list))
					{
						byGroup[groupId] = list = [];
					}

					list.Add(payload);
				}

				if (entity is Member member)
				{
					// The member may have joined another group: move their connections to its channel.
					await UpdateJoinedSubscriptionsAsync(member);
				}

				if (entity is Membership membership)
				{
					// Subscribe (or unsubscribe) the member's live connections before anything is sent to the
					// group, so a write right after an invite can't overtake it.
					await UpdateGroupSubscriptionsAsync(membership);
					// A removed member has just left the group, so tell them directly; anyone else gets it through the group.
					if (membership.Removed)
					{
						await hub.Clients.User(membership.MemberId.ToString()).SendAsync(method, new[] { payload });
					}
				}
			}

			foreach (var (groupId, entities) in byGroup)
			{
				await hub.Clients.GroupExcept(LyriousHub.GroupName(groupId), excluded).SendAsync(method, entities.ToArray());
			}
		}
		catch (Exception ex)
		{
			// Notifications are best-effort: the write itself has succeeded.
			logger.LogError(ex, "Failed to notify {EntityType} changes", typeof(TEntity).Name);
		}
	}

	/// <summary>
	/// Puts the member's live connections in the "joined" channel of their joined group only. The
	/// repository's identity is that member when they change their joined group; an admin renaming
	/// them doesn't change it, and what's done here then leaves their channel as it was.
	/// </summary>
	private async Task UpdateJoinedSubscriptionsAsync(Member member)
	{
		var connections = registry.GetConnections(member.Id.ToString());
		if (connections.Count == 0)
		{
			return;
		}

		foreach (var connectionId in connections)
		{
			foreach (var groupId in repo.GetMyGroupIds())
			{
				await hub.Groups.RemoveFromGroupAsync(connectionId, LyriousHub.JoinedGroupName(groupId));
			}

			if (member.JoinedGroupId is { } joined && repo.IsMember(joined))
			{
				await hub.Groups.AddToGroupAsync(connectionId, LyriousHub.JoinedGroupName(joined));
			}
		}
	}

	private async Task UpdateGroupSubscriptionsAsync(Membership membership)
	{
		var groupName = LyriousHub.GroupName(membership.GroupId);
		foreach (var connectionId in registry.GetConnections(membership.MemberId.ToString()))
		{
			if (membership.Removed)
			{
				await hub.Groups.RemoveFromGroupAsync(connectionId, groupName);
			}
			else
			{
				await hub.Groups.AddToGroupAsync(connectionId, groupName);
			}
		}
	}

	// Members are sent as a public profile only; Identity fields (password hash etc.) never leave the server.
	private static TEntity ToPayload<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		if (entity is not Member member)
		{
			return entity;
		}

		var profile = member.ToPublicProfile();
		return (TEntity)(IEntity)profile;
	}
}
