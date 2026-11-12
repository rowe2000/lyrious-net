using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lyrious.CoreLib;

public class LocalContext<TUser, TRole> : IdentityDbContext<TUser, TRole, Guid>, IRepository 
	where TUser : IdentityUser<Guid> 
	where TRole : IdentityRole<Guid> 
{
	private readonly Dictionary<Type, bool> initiated = new();
	private readonly Cache cache;

	public LocalContext(Cache cache)
	{
		this.cache = cache;
	}

	public LocalContext(DbContextOptions options, Cache cache) : base(options)
	{
		this.cache = cache;
	}

	public IEnumerable<TEntity> Get<TEntity>() where TEntity : class, IEntity, new()
	{
		//if (initiated.TryGetValue(typeof(TEntity), out var b) && b)
		//{
		//	return cache.Get<TEntity>();
		//}

		var entities = base.Set<TEntity>();
		//cache.Update(entities);
		//initiated[typeof(TEntity)] = true;
		return entities;
	}

	public void Change<TEntity>(object sender, ChangedArgs<TEntity> args) where TEntity : class, IEntity, new()
	{
		cache.Change(sender, args);
	}

	public IEnumerable<TEntity> Get<TEntity>(Func<TEntity, bool> p) where TEntity : class, IEntity, new()
	{
		//if (initiated[typeof(TEntity)])
		//{
		//	return cache.Get(p);
		//}

		var entities = Get<TEntity>().Where(p);
		//cache.Update(entities);
		return entities;
	}

	public IEnumerable<TEntity> Get<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		var enumerableIds = ids as Guid[] ?? ids.ToArray();
		var entities = cache.Get<TEntity>(enumerableIds).ToArray();
		if (entities.Length == enumerableIds.Length)
		{
			return entities;
		}
		
		entities = enumerableIds.Select(id => Find<TEntity>(id)).OfType<TEntity>().ToArray();
		if (entities.Any(_ => false))
		{
			return entities;
		}

		//cache.Update(entities.Select(o => o).ToArray());
		return entities;
	}

	public IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity, new()
	{
		var ret = new List<TEntity>();
		var db = Database.IsSqlite() ? "Sqlite" : "Sql server";

		WithoutGraphScanning(() =>
		{
			foreach (var entity in entities)
			{
				var existing = base.Find<TEntity>(entity.Id);
				if (existing != null)
				{
					// Copy the incoming [DataMember] values onto the tracked entity. Only [DataMember] properties
					// are copied, so Identity-managed fields on Member (password hash, security stamp, ...)
					// can't be overwritten by a client payload.
					Util.CopyProperties(entity, existing);
					// Stored rows always carry the checksum of their stored content, whatever the caller sent.
					existing.Checksum = ChecksumUtil.CalculateChecksum(existing);
					MarkChangedProperties(Entry(existing));
				}
				else
				{
					// Insert a fresh instance carrying all mapped scalar values (Identity fields included),
					// without attaching the caller's navigation graph.
					var entry = base.Add(new TEntity());
					entry.CurrentValues.SetValues(entity);
					existing = entry.Entity;
					existing.Checksum = ChecksumUtil.CalculateChecksum(existing);
				}

				ret.Add(existing);
			}

			try
			{
				SaveChangesOrRollback();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to store {typeof(TEntity).Name} entities to {db}: {ex.Message}");
				throw;
			}
		});

		Console.WriteLine($"Succeeded to store {typeof(TEntity).Name} entities to {db}");
		return ret;
	}

	/// <summary>
	/// Runs <paramref name="action"/> with automatic change detection off. Domain methods such as
	/// Group.CreateSongbook put caller-owned objects into navigation collections of tracked entities;
	/// a full DetectChanges would try to track those objects next to the copies stored here and fail
	/// with "another instance with the same key is already being tracked". Changes are marked
	/// explicitly instead, per entity.
	/// </summary>
	private void WithoutGraphScanning(Action action)
	{
		var autoDetectChanges = ChangeTracker.AutoDetectChangesEnabled;
		ChangeTracker.AutoDetectChangesEnabled = false;
		try
		{
			action();
		}
		finally
		{
			ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
		}
	}

	/// <summary>Marks the scalar properties whose value differs from the stored one as modified (no graph traversal).</summary>
	private static void MarkChangedProperties(EntityEntry entry)
	{
		foreach (var property in entry.Properties)
		{
			if (!property.Metadata.IsPrimaryKey() && !Equals(property.CurrentValue, property.OriginalValue))
			{
				property.IsModified = true;
			}
		}
	}

	/// <summary>
	/// Saves pending changes. On failure the pending changes are undone before rethrowing,
	/// so a failed save doesn't leave bad state behind that every later save on this context would retry.
	/// </summary>
	private void SaveChangesOrRollback()
	{
		try
		{
			SaveChanges();
		}
		catch
		{
			foreach (var entry in ChangeTracker.Entries().ToArray())
			{
				switch (entry.State)
				{
					case EntityState.Added:
						entry.State = EntityState.Detached;
						break;

					case EntityState.Modified:
					case EntityState.Deleted:
						entry.CurrentValues.SetValues(entry.OriginalValues);
						entry.State = EntityState.Unchanged;
						break;
				}
			}

			throw;
		}
	}


	// Add this helper method to handle related entities
	private void HandleRelatedEntities<TEntity>(TEntity entity) where TEntity : class, IEntity, new()
	{
		switch (entity)
		{
			// Handle Membership's special case with Member and Group references
			case Membership membership:
			{
				// Null out the navigation properties to avoid tracking conflicts
				var member = membership.Member;
				membership.Member = null;
				membership.MemberId = member.Id;
			
				var group = membership.Group;
				membership.Group = null;
				membership.GroupId = group.Id;
				break;
			}
		}
	}

	public void Remove<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new()
	{
		var guids = ids as IList<Guid> ?? ids.ToArray();
		
		//cache.Remove<TEntity>(guids);

		WithoutGraphScanning(() =>
		{
			foreach (var id in guids)
			{
				var entity = Set<TEntity>().Find(id);
				if (entity == null)
				{
					continue;
				}

				entity.Removed = true;
				ChecksumUtil.UpdateChecksum(entity);
				MarkChangedProperties(Entry(entity));
			}

			SaveChangesOrRollback();
		});
	}

	public IEnumerable<TEntity> Get<TEntity>(DateTime commit) where TEntity : class, IEntity, new()
	{
		//if (initiated[typeof(TEntity)])
		//{
		//	return cache.GetNewerThan<TEntity>(commit);
		//}

		return Get<TEntity>().Where(o => o.ChangedAt >= commit);
	}
	public IEnumerable<TEntity> Get<TEntity>(string name) where TEntity : class, IEntity, IName, new()
	{
		//if (initiated[typeof(TEntity)])
		//{
		//	return cache.GetNewerThan<TEntity>(commit);
		//}

		return Get<TEntity>().Where(o => o.Name == name);
	}
}