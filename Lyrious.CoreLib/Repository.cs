﻿using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.CoreLib;

public class Repository<TEntity, TContext>(TContext context) : IRepository<TEntity>
    where TEntity : EntityBase
    where TContext : DbContext
{
    public TEntity? Get(Guid id)
    {
        var value = Cache.Get<TEntity>(id);
        if (value == null)
        {
            value = context.Find<TEntity>(id);
            if (value is null)
                return null;
                
            // Send to server
        }

        return value;
    }

    public IEnumerable<TEntity> GetAll()
    {
        return Cache.GetAll<TEntity>();
    }

    public void Update(params TEntity[] updateEntities)
    {
        foreach (var updateEntity in updateEntities)
        {
            context.Update(updateEntity);
        }
            
        context.SaveChanges();

        // Send to server
    }

    public void Remove(params Guid[] ids)
    {
        foreach (var id in ids)
        {
            var entity = context.Find<TEntity>(id);
            if (entity is null)
                return;

            entity.Removed = true;
            context.Update(entity);
        }

        context.SaveChanges();
    }

    public void Remove(params TEntity[] removeEntities)
    {
        foreach (var removeEntity in removeEntities)
        {
            var entity = context.Find<TEntity>(removeEntity);
            if (entity is null)
                return;

            entity.Removed = true;
            context.Update(entity);
        }

        context.SaveChanges();

        // Send to server
    }

    public void Change(object sender, ChangedArgs<TEntity> args)
    {
        switch (args.Changed)
        {
            case Changed.Added:
            case Changed.Updated:
                Update(args.Values);
                break;
            case Changed.Removed:
                Remove(args.Values);
                break;
            case Changed.Cleared:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}