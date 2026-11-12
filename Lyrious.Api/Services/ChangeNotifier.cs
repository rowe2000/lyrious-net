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
				foreach (var groupId in repo.GroupIdsOf(entity))
				{
					if (!byGroup.TryGetValue(groupId, out var list))
					{
						byGroup[groupId] = list = [];
					}

					list.Add(payload);
				}

				if (entity is Membership membership)
				{
					// Subscribe (or unsubscribe) the member's live connections before anything is sent to the
					// group, so a write right after an invite can't overtake it.
					await UpdateGroupSubscriptionsAsync(membership);
					// Tell the member directly too: when they're the one being removed they're no longer in the group.
					await hub.Clients.User(membership.MemberId.ToString()).SendAsync(method, new[] { payload });
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

		var profile = new Member
		{
			Id = member.Id,
			UserName = member.UserName,
			Name = member.Name,
			JoinedGroupId = member.JoinedGroupId,
			CreatedAt = member.CreatedAt,
			ChangedAt = member.ChangedAt,
			Removed = member.Removed,
			Checksum = member.Checksum,
		};
		return (TEntity)(IEntity)profile;
	}
}
