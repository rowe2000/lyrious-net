namespace Lyrious.CoreLib;

public interface IRepository<TEntity>
{
    void Change(object sender, ChangedArgs<TEntity> args);
}