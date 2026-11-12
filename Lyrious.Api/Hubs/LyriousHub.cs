using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lyrious.Api.Hubs;

/// <summary>
/// Pushes entity changes to clients. Changes are made through the REST API; after each successful write
/// <see cref="Services.ChangeNotifier"/> sends "Update{EntityType}" with the stored entities to the
/// SignalR group of the Lyrious group that owns them. Each connection joins the groups of its member's
/// memberships when it connects, and later ones through <see cref="SubscribeGroup"/>.
/// </summary>
[Authorize]
public class LyriousHub(LyriousRepository repo, HubConnectionRegistry registry) : Hub
{
	public const string Path = "/hubs/lyrious";

	public static string UpdateMethodName<TEntity>() where TEntity : IEntity => $"Update{typeof(TEntity).Name}";

	public static string GroupName(Guid groupId) => groupId.ToString();

	public override async Task OnConnectedAsync()
	{
		if (Context.UserIdentifier is { } userId)
		{
			registry.Add(userId, Context.ConnectionId);
		}

		repo.SetIdentity(Context.User?.Identity);
		foreach (var groupId in repo.GetMyGroupIds())
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(groupId));
		}

		await base.OnConnectedAsync();
	}

	public override Task OnDisconnectedAsync(Exception? exception)
	{
		if (Context.UserIdentifier is { } userId)
		{
			registry.Remove(userId, Context.ConnectionId);
		}

		return base.OnDisconnectedAsync(exception);
	}

	/// <summary>
	/// Starts receiving changes for a group, e.g. after being invited while connected.
	/// Returns false when the caller isn't a member of the group.
	/// </summary>
	public async Task<bool> SubscribeGroup(Guid groupId)
	{
		repo.SetIdentity(Context.User?.Identity);
		if (!repo.IsMember(groupId))
		{
			return false;
		}

		await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(groupId));
		return true;
	}
}

/// <summary>Identifies hub users by member id (JWT "sub"), so notifications can target a member.</summary>
public sealed class MemberIdUserIdProvider : IUserIdProvider
{
	public string? GetUserId(HubConnectionContext connection)
	{
		return connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
		       ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
	}
}
