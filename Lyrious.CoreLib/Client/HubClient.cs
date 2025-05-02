using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Lyrious.CoreLib.Client;

public class HubClient
{
    private readonly HubConnection connection;

    private readonly Cache cache;

	public HubClient(string url, Cache cache)
    {
	    this.cache = cache;
	    connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;
        connection.Closed += OnClosed;
    }

    public IDisposable OnRemoteUpdates<TEntity>() where TEntity : class, IEntity, new()
    {
        return connection.On<TEntity[]>($"Update{typeof(TEntity).Name}", UpdateCache);
    }

    private void UpdateCache<TEntity>(TEntity[] entities) where TEntity : class, IEntity, new()
    {
        cache.Update(entities);
        Console.WriteLine($"Cache {typeof(TEntity).Name} updated with {entities.Length} items");
    }

    public void Update<TEntity>(object sender, ChangedArgs<TEntity> args)
    {
        connection.InvokeAsync($"Update{typeof(TEntity).Name}Async", args.Values);
        Console.WriteLine($"Remote {typeof(TEntity).Name} updated with {args.Values.Length} items");
    }

    protected virtual Task OnReconnected(string? s)
    {
        Reconnected?.Invoke(this, s);
        return Task.CompletedTask;
    }

    protected virtual Task OnReconnecting(Exception? arg)
    {
        Reconnecting?.Invoke(this, arg);
        return Task.CompletedTask;
    }

    protected virtual Task OnClosed(Exception? arg)
    {
        Closed?.Invoke(this, arg);
        return Task.CompletedTask;
    }

    public event Action<object, Exception?>? Reconnecting;
    public event Action<object, string?>? Reconnected;
    public event Action<object, Exception?>? Closed;

    public void BindCache<TEntity>() where TEntity : class, IEntity, new()
    {
        CacheSet<TEntity>.Changed += Update;
        OnRemoteUpdates<TEntity>();
    }

    public void UnbindCache<TEntity>() where TEntity : class, IEntity, new()
    {
        CacheSet<TEntity>.Changed -= Update;
    }
}