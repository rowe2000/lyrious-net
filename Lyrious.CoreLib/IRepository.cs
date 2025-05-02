using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib;


public interface IRepository
{
    void Change<TEntity>(object sender, ChangedArgs<TEntity> args) where TEntity : class, IEntity, new();
    IEnumerable<TEntity> Get<TEntity>() where TEntity : class, IEntity, new();
    IEnumerable<TEntity> Get<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new();
    IEnumerable<TEntity> Get<TEntity>(DateTime commit) where TEntity : class, IEntity, new();
    IEnumerable<TEntity> Get<TEntity>(Func<TEntity, bool> p) where TEntity : class, IEntity, new();
	IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity, new();
    void Remove<TEntity>(IEnumerable<Guid> ids) where TEntity : class, IEntity, new();
}