using System.Reflection;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.CoreLib;

public class Repository<TContext, TUser, TRole> : IRepository
	where TContext : LocalContext<TUser, TRole> 
	where TUser : IdentityUser<Guid> 
	where TRole : IdentityRole<Guid> 
{
	private readonly TContext dbContext;
	private readonly ApiClient remoteApi = new();
	private readonly Dictionary<Type, bool> synced = new();

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

	public async Task<Member?> RegisterAsync(RegisterModel model)
	{
		var member = await remoteApi.RegisterAsync(model);
		if (member != null)
		{
			dbContext.Update([member]);
		}
		
		Console.WriteLine(member);
		return member;
	}

	public async Task<Member?> LoginAsync(LoginModel loginModel)
	{
		var member = await remoteApi.LoginAsync(loginModel);
		Console.WriteLine(member);
		Me = member;
		return member;
	}

	public Member? Me { get; set; }

	public async Task<string> LogoutAsync()
	{
		var result = await remoteApi.LogoutAsync();
		Console.WriteLine(result);
		return result;
	}


	public void Change<TEntity>(object sender, ChangedArgs<TEntity> args) where TEntity : class, IEntity, new()
	{
		throw new NotImplementedException();
	}

	public IEnumerable<TEntity> Get<TEntity>(Func<TEntity, bool> p) where TEntity : class, IEntity, new()
	{
		if (synced.TryGetValue(typeof(TEntity), out var b) && b)
		{
			SyncRemote<TEntity>();
		}

		return dbContext.Get(p);
	}

	public IEnumerable<TEntity> Get<TEntity>() where TEntity : class, IEntity, new()
	{
		if (synced.TryGetValue(typeof(TEntity), out var b) && b )
		{
			SyncRemote<TEntity>();
		}

		return dbContext.Get<TEntity>();
	}

	private void SyncRemote<TEntity>() where TEntity : class, IEntity, new()
	{
		synced[typeof(TEntity)] = true;
	}

	public IEnumerable<TEntity> Get<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		var entities = remoteApi.Get<TEntity>(ids).ToArray();
		dbContext.Update(entities);
		return entities;
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
		//			local.Update([remoteEntity]);
		//		}
		//	}
		//}

		return tuples;
	}

	public IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity, new()
	{
		var array = entities.AsArray();
		dbContext.Update(array);
		remoteApi.Update(array);
		return array;
	}

	public TEntity? Update<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		dbContext.Update([entity]);
		remoteApi.Update([entity]);

		return entity;
	}

	public void Remove<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		var array = ids.AsArray();
		dbContext.Remove<TEntity>(array);
		remoteApi.Remove<TEntity>(array);
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