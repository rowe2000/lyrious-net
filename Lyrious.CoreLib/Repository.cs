using System.Collections.Concurrent;
using System.Reflection;
using Lyrious.CoreLib.Client;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.CoreLib;

public abstract class Repository<TContext, TUser, TRole> : IRepository
	where TContext : LocalContext<TUser, TRole>
	where TUser : IdentityUser<Guid>
	where TRole : IdentityRole<Guid>
{
	private readonly TContext dbContext;
	private readonly ApiClient remoteApi = new();
	// Concurrent: hub notifications clear entries from SignalR threads.
	private readonly ConcurrentDictionary<Type, bool> synced = new();
	private HubClient? hub;

	public Member? Me { get; set; }

	public string RemoteAddress
	{
		get => remoteApi.BaseUrl;
		set
		{
			remoteApi.BaseUrl = value;
			hub = null;
		}
	}

	/// <summary>
	/// Raised (on a SignalR thread) when the server pushes changes for the current member's groups.
	/// The next read of that entity type fetches fresh data from the remote.
	/// </summary>
	public event Action<Type, IEntity[]>? RemoteChanged;

	private HubClient Hub => hub ??= CreateHub();

	private HubClient CreateHub()
	{
		var hubClient = new HubClient(remoteApi.BaseUrl, () => remoteApi.AccessToken);
		hubClient.EntitiesChanged += OnRemoteChanged;
		hubClient.Reconnected += connectionId =>
		{
			// Pushes sent while disconnected are lost: re-fetch everything on the next reads.
			synced.Clear();
			remoteApi.HubConnectionId = connectionId;
		};
		return hubClient;
	}

	private void OnRemoteChanged(Type entityType, IEntity[] entities)
	{
		// Mark stale rather than writing to the DbContext here: this runs on a SignalR thread,
		// and the DbContext isn't thread-safe.
		synced.TryRemove(entityType, out _);

		// No need to subscribe to a group after being invited: the server adds this connection itself.
		RemoteChanged?.Invoke(entityType, entities);
	}

	private async Task ConnectHubAsync()
	{
		if (!HasRemote)
		{
			return;
		}

		try
		{
			await Hub.StartAsync();
			remoteApi.HubConnectionId = Hub.ConnectionId;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Live updates unavailable: {ex.Message}");
		}
	}

	private async Task DisconnectHubAsync()
	{
		remoteApi.HubConnectionId = null;
		if (hub == null)
		{
			return;
		}

		try
		{
			await hub.StopAsync();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to stop live updates: {ex.Message}");
		}
	}

	public Repository(TContext dbContext)
	{
		this.dbContext = dbContext;
		this.dbContext.Database.EnsureCreated();

		var dbSetProperties = dbContext.GetType()
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.PropertyType.IsGenericType &&
						p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

		foreach (var property in dbSetProperties)
		{
			Console.WriteLine($"DbSet: {property.GetType()}, Entity Type: {property.PropertyType.GenericTypeArguments[0].Name}");
		}
	}

	public async Task<Member?> RegisterAsync(string userName, string password, string name, string? email = null, string? phoneNumber = null)
	{
		email ??= userName + "@westman.st";
		phoneNumber ??= "";
		var registerModel = RegisterModel.Create(userName, password, name, email, phoneNumber);
		var member = await remoteApi.RegisterAsync(registerModel);
		if (member != null)
		{
			Store([member]);
		}

		Console.WriteLine(member);
		return member;
	}

	public async Task<Member?> LoginAsync(string userName, string password)
	{
		// A previous member's live connection must not keep receiving their groups' changes.
		await DisconnectHubAsync();

		var member = await remoteApi.LoginAsync(new LoginModel { Username = userName, Password = password });
		Console.WriteLine(member);
		if (member != null)
		{
			Me = member;
			// What the remote returns depends on who is logged in, so sync again for this member.
			synced.Clear();
			Synchronize(Me);
			await ConnectHubAsync();
		}

		return member;
	}

	/// <summary>
	/// Looks up a member by user name, email or phone number (in that order of precedence).
	/// Unlike Get&lt;Member&gt;, this isn't limited to members sharing a group with the current
	/// member, so it can be used to find someone to invite.
	/// </summary>
	public async Task<Member?> FindMemberAsync(string query)
	{
		if (HasRemote)
		{
			var remoteMember = await remoteApi.FindMemberAsync(query);
			if (remoteMember == null)
			{
				return null;
			}

			// Keep a local row so local memberships can reference it, but don't let the
			// public profile overwrite a fuller local copy.
			return dbContext.Find<Member>(remoteMember.Id) ?? Store([remoteMember]).FirstOrDefault() ?? remoteMember;
		}

		var members = dbContext.Set<Member>();
		return members.FirstOrDefault(o => o.UserName == query)
		       ?? members.FirstOrDefault(o => o.Email == query)
		       ?? members.FirstOrDefault(o => o.PhoneNumber == query);
	}

	public void Synchronize(Member me)
	{
		var memberships = Get<Membership>(o => o.MemberId == me.Id).ToArray();
		
		var memberIds = new HashSet<Guid>(memberships.Select(o => o.MemberId));
		var members = Get<Member>(memberIds);
		
		var groupIds = new HashSet<Guid>(memberships.Select(o => o.GroupId));
		var groups = Get<Group>(groupIds);

		var setListIds = new HashSet<Guid>(Get<Setlist>(o => groupIds.Contains(o.GroupId)).Select(o => o.Id));
		var setlistItems = Get<SetlistItem>(o => setListIds.Contains(o.SetlistId));

		var songbookIds = new HashSet<Guid>(Get<Songbook>(o => groupIds.Contains(o.GroupId ?? Guid.Empty)).Select(o => o.Id));
		var songs = Get<Song>(o => songbookIds.Contains(o.SongbookId));

		var playlogIds = new HashSet<Guid>(Get<Playlog>(o => groupIds.Contains(o.GroupId)).Select(o => o.Id));
		var plays = Get<Play>(o => playlogIds.Contains(o.PlaylogId));
	}

	public async Task LogoutAsync()
	{
		await DisconnectHubAsync();
		await remoteApi.LogoutAsync();
		Me = null;
		synced.Clear();
	}


	public void Change<TEntity>(object sender, ChangedArgs<TEntity> args) where TEntity : class, IEntity, new()
	{
		throw new NotImplementedException();
	}

	public IEnumerable<TEntity> Get<TEntity>(Func<TEntity, bool> p) where TEntity : class, IEntity, new()
	{
		SyncRemote<TEntity>();
		return dbContext.Get(p).Where(HasAccess);
	}

	protected abstract bool HasAccess<TEntity>(TEntity arg) where TEntity : class, IEntity, new();
	

	public IEnumerable<TEntity> Get<TEntity>() where TEntity : class, IEntity, new()
	{
		SyncRemote<TEntity>();
		return dbContext.Get<TEntity>().Where(HasAccess);
	}

	public bool HasRemote => !string.IsNullOrWhiteSpace(remoteApi.BaseUrl);

	/// <summary>
	/// Writes entities to the local database. Without a remote (the API server) the local database
	/// is authoritative and failures propagate. With a remote it is only a local copy, so a failure
	/// is logged and an empty result returned, leaving the remote write to go ahead.
	/// </summary>
	private TEntity[] Store<TEntity>(TEntity[] entities) where TEntity : class, IEntity, new()
	{
		if (!HasRemote)
		{
			return dbContext.Update(entities).AsArray();
		}

		try
		{
			return dbContext.Update(entities).AsArray();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to cache {typeof(TEntity).Name} entities locally: {ex.Message}");
			return [];
		}
	}

	private void SyncRemote<TEntity>() where TEntity : class, IEntity, new()
	{
		if (!HasRemote)
		{
			return;
		}

		var needSync = synced.TryGetValue(typeof(TEntity), out var b) && b;
		if (needSync)
		{
			return;
		}

		var entities = remoteApi.Get<TEntity>();
		Store(entities.AsArray());
		synced[typeof(TEntity)] = true;
	}


	public TEntity? Get<TEntity>(Guid id) where TEntity : class, IEntity, new()
	{
		return Get<TEntity>([id]).SingleOrDefault();
	}


	public IEnumerable<TEntity> Get<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		ids = ids.AsArray();

		if (!ids.Any())
		{
			return [];
		}

		if (HasRemote && !(synced.TryGetValue(typeof(TEntity), out var b) && b))
		{
			var entities = remoteApi.Get<TEntity>(ids).ToArray();
			Store(entities);
			return entities.Where(HasAccess);
		}

		var tuples = dbContext.Get<TEntity>(ids).Where(HasAccess);
		//var remoteIds = tuples.Where(o => o == null).ToArray();
		//if (remoteIds.Any())
		//{
		//	var remoteEntities = (await remote.Get<TEntity>(remoteIds));
		//	foreach (var (remoteId, remoteEntity) in remoteEntities)
		//	{
		//		if (remoteEntity != null)
		//		{
		//			tuples[remoteId] = remoteEntity;
		//			local.Update([remo
		//			teEntity]);
		//		}
		//	}
		//}

		return tuples;
	}

	public IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity, new()
	{
		var array = entities.AsArray();
		array.ForEach(o => ChecksumUtil.UpdateChecksum(o));
		Store(array);
		if (HasRemote)
		{
			remoteApi.Update(array);
		}

		return array;
	}

	public TEntity? Update<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		ChecksumUtil.UpdateChecksum(entity);
		var dbEntity = Store([entity]).FirstOrDefault();
		if (HasRemote)
		{
			remoteApi.Update([entity]);
			// The remote is authoritative; a failed local copy shouldn't make the write look failed.
			return dbEntity ?? entity;
		}

		return dbEntity;
	}

	public void Remove<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		var array = ids.AsArray();
		dbContext.Remove<TEntity>(array);
		if (HasRemote)
		{
			remoteApi.Remove<TEntity>(array);
		}
	}

	public IEnumerable<TEntity> Get<TEntity>(DateTime commit) where TEntity : class, IEntity, new()
	{
		return dbContext.Get<TEntity>(commit);
	}
	
	public IEnumerable<TEntity> Get<TEntity>(string name) where TEntity : class, IEntity, IName, new()
	{
		// Same path as the other reads: sync from the remote and apply access checks.
		return Get<TEntity>(o => o.Name == name);
	}
}