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
/// <para>
/// Each connection is also in the "joined" channel of its member's <see cref="Member.JoinedGroupId"/>:
/// the one group the member follows live (play/stop, see <see cref="SetPlayback"/>). It moves when
/// the member joins another group (see <see cref="Services.ChangeNotifier"/>).
/// </para>
/// </summary>
[Authorize]
public class LyriousHub(LyriousRepository repo, HubConnectionRegistry registry) : Hub
{
	public const string Path = "/hubs/lyrious";

	public static string UpdateMethodName<TEntity>() where TEntity : IEntity => $"Update{typeof(TEntity).Name}";

	public static string GroupName(Guid groupId) => groupId.ToString();

	/// <summary>The channel of the members that have joined (follow live) the group.</summary>
	public static string JoinedGroupName(Guid groupId) => $"joined:{groupId}";

	/// <summary>Sent to a joined group's channel when one of its members starts or stops playing: (groupId, playing).</summary>
	public const string PlaybackChangedMethod = "PlaybackChanged";

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

		if (repo.Me?.JoinedGroupId is { } joined && repo.IsMember(joined))
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, JoinedGroupName(joined));
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
	/// Tells the other members that have joined the caller's joined group that playback started or
	/// stopped. Nothing is stored. Returns false when the caller hasn't joined a group they're a member of.
	/// </summary>
	public async Task<bool> SetPlayback(bool playing)
	{
		repo.SetIdentity(Context.User?.Identity);
		if (repo.Me?.JoinedGroupId is not { } joined || !repo.IsMember(joined))
		{
			return false;
		}

		await Clients.GroupExcept(JoinedGroupName(joined), [Context.ConnectionId]).SendAsync(PlaybackChangedMethod, joined, playing);
		return true;
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
