using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib;

public class Cache : IRepository
{
    private readonly Dictionary<Type, object> cacheSets = new();

    private CacheSet<TEntity> GetCacheSet<TEntity>() where TEntity : class, IEntity, new()
    {
	    if (cacheSets.TryGetValue(typeof(TEntity), out var cache))
        {
            return (CacheSet<TEntity>)cache;
        }

        var c = new CacheSet<TEntity>();
        cacheSets.Add(typeof(TEntity), c);
        return c;
    }

    public IEnumerable<TEntity> Get<TEntity>() 
	    where TEntity : class, IEntity, new()
    {
	    return GetCacheSet<TEntity>().Get();
    }

    public TEntity? Get<TEntity>(Guid id)
	    where TEntity : class, IEntity, new()
    {
	    return GetCacheSet<TEntity>().Get([id]).FirstOrDefault();
    }
    public IEnumerable<TEntity> Get<TEntity>(IEnumerable<Guid> ids) 
	    where TEntity : class, IEntity, new()
    {
	    return GetCacheSet<TEntity>().Get(ids);
    }

    public IEnumerable<TEntity> Get<TEntity>(Func<TEntity, bool> predicate)
	    where TEntity : class, IEntity, new()
    {
	    return GetCacheSet<TEntity>().Get(predicate);
    }

    public IEnumerable<TEntity> Get<TEntity>(DateTime from) 
	    where TEntity : class, IEntity, new()
    {
        return GetCacheSet<TEntity>().Get(from);
    }

    public TEntity? Remove<TEntity>(Guid id)
	    where TEntity : class, IEntity, new()
    {
	    return GetCacheSet<TEntity>().Remove(id);
    }

    public void Remove<TEntity>(IEnumerable<Guid> ids) 
	    where TEntity : class, IEntity, new()
    {
	    GetCacheSet<TEntity>().Remove(ids);
    }

    public IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities) 
	    where TEntity : class, IEntity, new()
    {
        return Update(entities, false);
    }

    public IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> values, bool noEvent) 
		where TEntity : class, IEntity, new()
	{
        return GetCacheSet<TEntity>().Update(values, noEvent);
    }

    public TEntity Update<TEntity>(Guid id, TEntity entity) 
	    where TEntity : class, IEntity, new()
    {
	    return GetCacheSet<TEntity>().Update(id, entity);
    }

    public void Bind<TEntity>(IRepository repository) 
	    where TEntity : class, IEntity, new()
    {
        GetCacheSet<TEntity>().Bind(repository);
    }

    public void Change<TEntity>(object sender, ChangedArgs<TEntity> args) 
	    where TEntity : class, IEntity, new()
    {
		GetCacheSet<TEntity>().Change(sender, args);
	}
}