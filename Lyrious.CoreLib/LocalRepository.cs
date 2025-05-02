using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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

		var entities = Get<TEntity>().Where(p).ToArray();
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
		try
		{
			foreach (var entity in entities)
			{
				var existing = base.Find<TEntity>(entity.Id);
				if (existing != null)
				{
					var entry = base.Update(existing);
					existing = entry.Entity;
				}
				else
				{
					var clone = entity.Clone<TEntity>();
					var entry = base.Add(clone);
					existing = entry.Entity;
				}

				ret.Add(existing);
			}

			SaveChanges();
			Console.WriteLine($"Succeeded to store {typeof(TEntity).Name} entities to {db}");

			return ret;
		}
		//catch (DbUpdateConcurrencyException ex)
		//{
		//	Console.WriteLine($"Concurrency conflict: {ex.Message}");

		//	foreach (var entry in ex.Entries)
		//	{
		//		if (entry.Entity is TEntity conflictingEntity)
		//		{
		//			// Reload the entity from the database
		//			entry.Reload();
		//		}
		//	}

		//	// Retry saving changes
		//	SaveChanges();
		//	return entities;
		//}
		//catch (DbUpdateException ex)
		//{
		//	Console.WriteLine($"Update error: {ex.Message}");

		//	// Check specifically for foreign key violations
		//	if (ex.InnerException?.Message.Contains("FOREIGN KEY constraint") == true)
		//	{
		//		// For foreign key violations, retry with a transaction
		//		using (var transaction = Database.BeginTransaction())
		//		{
		//			try
		//			{
		//				// Resubmit the changes in proper order
		//				// First identify the entity types
		//				foreach (var entry in ChangeTracker.Entries())
		//				{
		//					if (entry.State == EntityState.Added)
		//					{
		//						// Process only entities that aren't the primary entity type
		//						if (!(entry.Entity is TEntity))
		//						{
		//							// Save these non-TEntity changes first
		//							SaveChanges();
		//							break;
		//						}
		//					}
		//				}

		//				// Now try to save the main entities again
		//				SaveChanges();
		//				transaction.Commit();
		//				return entities;
		//			}
		//			catch (Exception transactionEx)
		//			{
		//				transaction.Rollback();
		//				Console.WriteLine($"Transaction failed: {transactionEx.Message}");
		//				throw;
		//			}
		//		}
		//	}

		//	// If it's another type of update exception, just log and rethrow
		//	throw;
		//}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to store {typeof(TEntity).Name} entities to {db}: {ex.Message}");
		}

		return [];
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
		
		foreach (var id in guids)
		{
			var entity = Set<TEntity>().Find(id);
			if (entity == null)
			{
				continue;
			}

			entity.Removed = true;
			Update(entity);
		}

		SaveChanges();
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