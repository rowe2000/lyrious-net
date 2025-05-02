using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib;

public class CacheSet<TEntity> where TEntity : class, IEntity, new()
{
	private readonly Dictionary<Guid, TEntity> entities = new();

	public static event Action<object, ChangedArgs<TEntity>>? Changed;

	private void OnChanged(ChangedEnum changedEnum, IEnumerable<TEntity> changedEntities)
	{
		Changed?.Invoke(this, new ChangedArgs<TEntity>(changedEnum, changedEntities));
	}

	public IEnumerable<TEntity> Get()
	{
		return entities.Values;
	}

	public IEnumerable<TEntity> Get(IEnumerable<Guid> ids)
	{
		return ids.Select(id => entities.TryGetValue(id, out var value) ? value : null).OfType<TEntity>();
	}

	public IEnumerable<TEntity> Get(Func<TEntity, bool> predicate)
	{
		return entities.Values.Where(predicate);
	}

	public IEnumerable<TEntity> Get(DateTime from)
	{
		return entities.Values.Where(o => o.ChangedAt > from);
	}

	public IEnumerable<TEntity> Remove(IEnumerable<Guid> ids)
	{
		var removeEntities = new List<TEntity>();
		foreach (var id in ids)
		{
			var entity = entities.TryGetValue(id, out var value) && entities.Remove(id) ? value : null;
			if (entity is not null)
			{
				removeEntities.Add(entity);
			}
		}

		OnChanged(Enums.ChangedEnum.Remove, removeEntities);
		return removeEntities;
	}

	public TEntity? Remove(Guid id)
	{
		return Remove([id]).FirstOrDefault();
	}

	public TEntity? Remove(TEntity entity)
	{
		return Remove([entity.Id]).FirstOrDefault();
	}

	public IEnumerable<TEntity> Remove(IEnumerable<TEntity> pendingRemovedEntities, bool noEvent = false)
	{
		return Remove(pendingRemovedEntities.Select(o => o.Id), noEvent);
	}

	public IEnumerable<TEntity> Remove(IEnumerable<Guid> pendingRemovedIds, bool noEvent)
	{
		List<TEntity> removedEntities = [];
		foreach (var id in pendingRemovedIds)
		{
			if (entities.Remove(id, out var removedEntity))
			{
				removedEntities.Add(removedEntity);
			}
		}

		if (!noEvent)
		{
			OnChanged(Enums.ChangedEnum.Remove, removedEntities);
		}

		return removedEntities;
	}

	public IEnumerable<TEntity> Update(IEnumerable<TEntity> updatedEntities, bool noEvent = false)
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
			OnChanged(Enums.ChangedEnum.Update, changedItems);
		}

		Console.WriteLine($"Updated items {string.Join(Environment.NewLine, changedItems.Select(o => $"{o}"))}");
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
			OnChanged(Enums.ChangedEnum.Add, list);
		}

		return list;
	}

	public TEntity Update(Guid id, TEntity value)
	{
		entities.Add(id, value);
		return value;
	}

	public void Bind(IRepository repository)
	{
		Changed += repository.Change;
	}

	public void Change(object sender, ChangedArgs<TEntity> args)
	{
		switch (args.ChangedEnum)
		{
			case Enums.ChangedEnum.Add:
			case Enums.ChangedEnum.Insert:
				Add(args.Values);
				break;
			case Enums.ChangedEnum.Update:
				break;
			case Enums.ChangedEnum.Remove:
				Remove(args.Values);
				break;
			case Enums.ChangedEnum.Clear:
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}
	}
}