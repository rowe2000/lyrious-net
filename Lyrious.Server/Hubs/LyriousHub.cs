using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.SignalR;

namespace Lyrious.Server.Hubs;

public class LyriousHub : Hub
{
    private static readonly BiDictionary<string, Member> ConnectionIdToMember = new();
    private readonly Cache cache;
    public LyriousHub(Cache cache)
    {
	    this.cache = cache;
        CacheSet<Group>.Changed += Changed;
        CacheSet<Member>.Changed += Changed;
        CacheSet<Membership>.Changed += Changed;
        CacheSet<Play>.Changed += Changed;
        CacheSet<Playlog>.Changed += Changed;
        CacheSet<Setlist>.Changed += Changed;
        CacheSet<SetlistItem>.Changed += Changed;
        CacheSet<Song>.Changed += Changed;
        CacheSet<Songbook>.Changed += Changed;
    }

    private static string UpdateName<TEntity>() 
        where TEntity : IEntity
    {
        return $"Update{typeof(TEntity).Name}";
    }


    public override Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectionIdToMember.Remove(Context.ConnectionId);
        return Task.CompletedTask;
    }

    private void Changed<T>(object sender, ChangedArgs<T> args) where T : IEntity
    {
        Clients.Caller.SendAsync(UpdateName<T>(), args.Values);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CacheSet<Group>.Changed -= Changed;
            CacheSet<Member>.Changed -= Changed;
            CacheSet<Membership>.Changed -= Changed;
            CacheSet<Play>.Changed -= Changed;
            CacheSet<Playlog>.Changed -= Changed;
            CacheSet<Setlist>.Changed -= Changed;
            CacheSet<SetlistItem>.Changed -= Changed;
            CacheSet<Song>.Changed -= Changed;
            CacheSet<Songbook>.Changed -= Changed;
        }
        base.Dispose(disposing);
    }


    public async Task LogMeIn(Guid memberId)
    {
        var member = cache.Get<Member>(memberId);
        if (member is null)
            return;

        ConnectionIdToMember.Add(Context.ConnectionId, member);

        var joinedGroupId = member.JoinedGroupId;
        if (joinedGroupId is null)
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, joinedGroupId.ToString());
    }


    public async Task SendMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
    
    public async Task UpdateGroupAsync(Group[] entities) => await Update(entities);
    public async Task UpdateMemberAsync(Member[] entities) => await Update(entities);
    public async Task UpdateMembershipAsync(Membership[] entities) => await Update(entities);
    public async Task UpdatePlayAsync(Play[] entities) => await Update(entities);
    public async Task UpdatePlaylogAsync(Playlog[] entities) => await Update(entities);
    public async Task UpdateSetlistAsync(Setlist[] entities) => await Update(entities);
    public async Task UpdateSetlistItemAsync(SetlistItem[] entities) => await Update(entities);
    public async Task UpdateSongAsync(Song[] entities) => await Update(entities);
    public async Task UpdateSongbookAsync(Songbook[] entities) => await Update(entities);

    public async Task JoinGroupAsync(Guid groupId)
    {
        var member = ConnectionIdToMember[Context.ConnectionId];
        var group = cache.Get<Group>(groupId);

        if (!group.Memberships.Any(o => o.Member.Equals(member)))
            return;
        
        if (member.JoinedGroupId != groupId)
        {
            await Clients
                .OthersInGroup(groupId.ToString())
                .SendAsync("MemberLeft", member.Id);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, member.JoinedGroupId.ToString());

            member.JoinedGroup = group;
			member.JoinedGroupId = group.Id;
            cache.Update([member]);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId.ToString());

        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("MemberJoined", member.Id);
    }

    public async Task SelectSetlistAsync(Guid setlistId, Guid groupId, DateTime timestamp)
    {
        var group = cache.Get<Group>(groupId);
        var setlist = cache.Get<Setlist>(setlistId);
        group.CurrentSetlist = setlist;

		cache.Update([group]);

		await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("SetlistSelected", setlist.Id);
    }

    public async Task SelectSongAsync(Guid setlistItemId, Guid groupId)
    {
        var group = cache.Get<Group>(groupId);
        var setlistItem = cache.Get<SetlistItem>(setlistItemId);
        if (group is null)
            return;

        group.CurrentSetlistItem = setlistItem;
        group.CurrentSetlistItemId = setlistItem?.Id;

		cache.Update([group]);

        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("SetlistItemSelected", setlistItem?.Id);
    }

    public async Task StartSongAsync(Guid groupId, Guid conductorId)
    {
        var group = cache.Get<Group>(groupId);
        var conductor = cache.Get<Member>(conductorId);
        if (group is null)
            return;
        
        if (conductor is null ||  group?.CurrentSetlistItem is null)
            return;

        var song = group.CurrentSetlistItem.Song;
        var playlog = group.Playlogs.Last();

        var play = Play.Create(conductor, playlog, song);
		playlog.Plays.Add(play);

		group.CurrentPlay = play;
		group.CurrentPlayId = play.Id;

		cache.Update([group]);

        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("PlayStarted", group.CurrentPlay);
    }
    
    private async Task Update<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class, IEntity, new()
    {
        cache.Update(entities.AsArray(), true);

        var dbContext = new LyriousContext(cache);
        var updatedEntities = new List<TEntity>();
        var returnEntities = new List<TEntity>();
            
        foreach (var entity in entities)
        {


            var existingEntity = dbContext.Find<TEntity>(entity.Id);
            if (existingEntity is null)
            {
                dbContext.Update(entity);
                updatedEntities.Add(entity);
            }
            else if (existingEntity.ChangedAt < entity.ChangedAt)
            {
                dbContext.Update(entity);
                updatedEntities.Add(entity);
            }
            if (existingEntity.Checksum == entity.Checksum)
            {
            }
            else
            {
                returnEntities.Add(existingEntity);
            }
        }
            
        if (updatedEntities.Any())
        {
            var groupId = ConnectionIdToMember[Context.ConnectionId].JoinedGroupId;
            await dbContext.SaveChangesAsync();
            await Clients.OthersInGroup(groupId.ToString()).SendAsync($"Update{typeof(TEntity).Name}", updatedEntities);
        }
        
        if (returnEntities.Any()) 
            await Clients.Caller.SendAsync($"Update{typeof(TEntity).Name}", returnEntities);
    }

    public async Task SyncAll(DateTime from)
    {
        await UpdateCallerClientFrom<Group>(from);
        await UpdateCallerClientFrom<Member>(from);
        await UpdateCallerClientFrom<Membership>(from);
        await UpdateCallerClientFrom<Play>(from);
        await UpdateCallerClientFrom<Playlog>(from);
        await UpdateCallerClientFrom<Setlist>(from);
        await UpdateCallerClientFrom<SetlistItem>(from);
        await UpdateCallerClientFrom<Song>(from);
        await UpdateCallerClientFrom<Songbook>(from);
    }

    private Task UpdateCallerClientFrom<TEntity>(DateTime from) where TEntity : class, IEntity, new()
    {
        var entities = cache.Get<TEntity>(from);
        var updateMethodName = UpdateName<TEntity>();
        return Clients.Caller.SendAsync(updateMethodName, entities);
    }
}
