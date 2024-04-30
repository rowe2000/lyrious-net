using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Lyrious.DataAccessLayer;
using Microsoft.AspNetCore.SignalR;

namespace Lyrious.Server.Hubs;

public class LyriousHub : Hub
{
    private static readonly BiDictionary<string, Member> ConnectionIdToMember = new();
    public LyriousHub()
    {
        Cache<Group>.Changed += Changed;
        Cache<Member>.Changed += Changed;
        Cache<Membership>.Changed += Changed;
        Cache<Play>.Changed += Changed;
        Cache<Playlog>.Changed += Changed;
        Cache<Setlist>.Changed += Changed;
        Cache<SetlistItem>.Changed += Changed;
        Cache<Song>.Changed += Changed;
        Cache<Songbook>.Changed += Changed;
    }

    private static string UpdateName<TEntity>() 
        where TEntity : EntityBase
    {
        return $"Update{typeof(TEntity).Name}";
    }


    public override Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectionIdToMember.Remove(Context.ConnectionId);
        return Task.CompletedTask;
    }

    private void Changed<T>(object sender, ChangedArgs<T> args) where T : EntityBase
    {
        Clients.Caller.SendAsync(UpdateName<T>(), args.Values);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cache<Group>.Changed -= Changed;
            Cache<Member>.Changed -= Changed;
            Cache<Membership>.Changed -= Changed;
            Cache<Play>.Changed -= Changed;
            Cache<Playlog>.Changed -= Changed;
            Cache<Setlist>.Changed -= Changed;
            Cache<SetlistItem>.Changed -= Changed;
            Cache<Song>.Changed -= Changed;
            Cache<Songbook>.Changed -= Changed;
        }
        base.Dispose(disposing);
    }


    public async Task LogMeIn(Guid memberId)
    {
        var member = Cache.Get<Member>(memberId);
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
        var group = Cache.Get<Group>(groupId);

        if (!group.Memberships.Any(o => o.Member.Equals(member)))
            return;
        
        if (member.JoinedGroupId != groupId)
        {
            await Clients
                .OthersInGroup(groupId.ToString())
                .SendAsync("MemberLeft", member.Id);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, member.JoinedGroupId.ToString());

            member.JoinedGroup = group;
            Cache.Update([member]);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId.ToString());

        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("MemberJoined", member.Id);
    }

    public async Task SelectSetlistAsync(Guid setlistId, Guid groupId, DateTime timestamp)
    {
        var group = Cache.Get<Group>(groupId);
        var setlist = Cache.Get<Setlist>(setlistId);
        var state = group.GroupState;
        state.CurrentSetlist = setlist;
        
        Cache.Update([state]);
        
        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("SetlistSelected", setlist.Id);
    }

    public async Task SelectSongAsync(Guid setlistItemId, Guid groupId)
    {
        var group = Cache.Get<Group>(groupId);
        var setlistItem = Cache.Get<SetlistItem>(setlistItemId);
        if (group is null)
            return;

        var state = group.GroupState;
        
        state.CurrentSetlistItem = setlistItem;
        
        Cache.Update([state]);

        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("SetlistItemSelected", setlistItem?.Id);
    }

    public async Task StartSongAsync(Guid groupId, Guid conductorId)
    {
        var group = Cache.Get<Group>(groupId);
        var conductor = Cache.Get<Member>(conductorId);
        if (group is null)
            return;
        
        var state = group.GroupState;
        if (conductor is null ||  state?.CurrentSetlistItem is null)
            return;

        var song = state.CurrentSetlistItem.Song;
        var playlog = group.Playlogs.Last();
        
        state.CurrentPlay = Play.Create(conductor, playlog, song);

        Cache.Update([state]);

        await Clients
            .OthersInGroup(groupId.ToString())
            .SendAsync("PlayStarted", state.CurrentPlay);
    }
    
    private async Task Update<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : EntityBase
    {
        Cache.Update(entities.AsArray(), true);

        var dbContext = new LyriousContext(DbContextType.Sqllite);
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

    private Task UpdateCallerClientFrom<TEntity>(DateTime from) where TEntity : EntityBase
    {
        var entities = Cache.GetAllFrom<TEntity>(from);
        var updateMethodName = UpdateName<TEntity>();
        return Clients.Caller.SendAsync(updateMethodName, entities);
    }
}
