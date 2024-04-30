using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib;

public class Cache
{
    private static readonly Dictionary<Type, Cache> Caches = new();

    protected static Cache<TEntity> GetCache<TEntity>() where TEntity : EntityBase
    {
        var type = typeof(TEntity);

        if (Caches.TryGetValue(type, out var cache))
        {
            return (Cache<TEntity>)cache;
        }

        var c = new Cache<TEntity>();
        Caches.Add(type, c);
        return c;
    }

    public static TEntity? Get<TEntity>(Guid id) where TEntity : EntityBase
    {
        return Cache<TEntity>.Instance.Get(id);
    }

    public static IEnumerable<TEntity> GetAll<TEntity>() where TEntity : EntityBase
    {
        return Cache<TEntity>.Instance.GetAll();
    }

    public static IEnumerable<TEntity> GetAllFrom<TEntity>(DateTime from) where TEntity : EntityBase
    {
        return Cache<TEntity>.Instance.GetAllFrom(from);
    }

    public static TEntity? Remove<TEntity>(Guid id) where TEntity : EntityBase
    {
        return Cache<TEntity>.Instance.Remove(id);
    }

    public static void Update<TEntity>(TEntity[] values, bool noEvent = false) where TEntity : EntityBase
    {
        Cache<TEntity>.Instance.Update(values, noEvent);
    }

    //public static void Update<TEntity>(Guid id, TEntity entity) where TEntity : EntityBase, new() => Cache<TEntity>.Instance.Update(id, entity);
    public static void Bind<TEntity>(IRepository<TEntity> repository) where TEntity : EntityBase
    {
        Cache<TEntity>.Instance.Bind(repository);
    }

    public static TEntity? GetNewerThan<TEntity>(DateTime commit) where TEntity : EntityBase, new()
    {
        return null;
    }
}

public class Cache<TEntity> : Cache, IRepository<TEntity> where TEntity : EntityBase
{
    private readonly Dictionary<Guid, TEntity> entities = new();

    private void OnChanged(Changed changed, IEnumerable<TEntity> changedEntities)
    {
        Changed?.Invoke(this, new ChangedArgs<TEntity>(changed, changedEntities));
    }

    public static readonly Cache<TEntity> Instance = GetCache<TEntity>();

    public static event Action<object, ChangedArgs<TEntity>>? Changed;

    public TEntity? Get(Guid id)
    {
        return entities.TryGetValue(id, out var value) ? value : null;
    }

    public IEnumerable<TEntity> GetAll()
    {
        return entities.Values;
    }

    public IEnumerable<TEntity> GetAllFrom(DateTime from)
    {
        return entities.Values.Where(o => o.ChangedAt > from);
    }

    public TEntity? Remove(Guid id)
    {
        var removedEntity = entities.TryGetValue(id, out var value) && entities.Remove(id) ? value : null;
        if (removedEntity is null)
        {
            return null;
        }

        OnChanged(Enums.Changed.Remove, [removedEntity]);
        return removedEntity;
    }

    public TEntity Remove(TEntity entity)
    {
        entities.Remove(entity.Id);
        OnChanged(Enums.Changed.Remove, [entity]);
        return entity;
    }

    public IList<TEntity> Remove(IEnumerable<TEntity> pendingRemovedEntities, bool noEvent = false)
    {
        List<TEntity> removedEntities = new();
        foreach (var entity in pendingRemovedEntities)
        {
            entities.Remove(entity.Id, out var removedEntity);
            removedEntities.Add(removedEntity);
        }

        if (!noEvent)
        {
            OnChanged(Enums.Changed.Remove, pendingRemovedEntities);
        }

        return removedEntities;
    }


    public IList<TEntity> Update(IEnumerable<TEntity> updatedEntities, bool noEvent = false)
    {
        var array = updatedEntities.AsArray();
        var changedItems = new List<TEntity>(array.Length);
        for (var i = 0; i < array.Length; i++)
        {
            var newEntity = array[i];
            entities.TryGetValue(array[i].Id, out var existingEntity);

            if (array[i].Checksum == existingEntity?.Checksum)
            {
                continue;
            }

            if (array[i].ChangedAt > existingEntity?.ChangedAt)
            {
                array[i] = existingEntity;
            }

            changedItems.Add(array[i]);
            entities.Remove(array[i].Id);
            entities.Add(array[i].Id, array[i]);
        }

        if (!noEvent)
        {
            OnChanged(Enums.Changed.Update, changedItems);
        }

        return changedItems;
    }

    public IList<TEntity> Add(IEnumerable<TEntity> addedEntities, bool noEvent = false)
    {
        var list = addedEntities.AsIList();
        foreach (var entity in list)
        {
            entities.Remove(entity.Id);
            entities.Add(entity.Id, entity);
        }

        if (!noEvent)
        {
            OnChanged(Enums.Changed.Add, list);
        }

        return list;
    }

    public void Update(Guid id, TEntity value)
    {
        entities.Add(id, value);
    }

    public void Bind(IRepository<TEntity> repository)
    {
        Changed += repository.Change;
    }

    public void Change(object sender, ChangedArgs<TEntity> args)
    {
        switch (args.Changed)
        {
            case Enums.Changed.Add:
            case Enums.Changed.Insert:
                Add(args.Values);
                break;
            case Enums.Changed.Update:
                break;
            case Enums.Changed.Remove:
                Remove(args.Values);
                break;
            case Enums.Changed.Clear:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}