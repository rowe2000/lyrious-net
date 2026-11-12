using System.Reflection;
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
	private readonly Dictionary<Type, bool> synced = new();

	public Member? Me { get; set; }

	public string RemoteAddress
	{
		get => remoteApi.BaseUrl;
		set => remoteApi.BaseUrl = value;
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
			dbContext.Update([member]);
		}

		Console.WriteLine(member);
		return member;
	}

	public async Task<Member?> LoginAsync(string userName, string password)
	{
		var member = await remoteApi.LoginAsync(new LoginModel { Username = userName, Password = password });
		Console.WriteLine(member);
		if (member != null)
		{
			Me = member;
			Synchronize(Me);
		}

		return member;
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
		await remoteApi.LogoutAsync();
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
		dbContext.Update(entities);
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
			dbContext.Update(entities);
			return entities;
		}

		var tuples = dbContext.Get<TEntity>(ids);
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
		dbContext.Update(array);
		if (HasRemote)
		{
			remoteApi.Update(array);
		}

		return array;
	}

	public TEntity? Update<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		var dbEntity = dbContext.Update([entity]).FirstOrDefault();
		if (HasRemote)
		{
			remoteApi.Update([entity]);
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
		return dbContext.Get<TEntity>(name);
	}
}