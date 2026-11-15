using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Lyrious.UILib;

namespace Lyrious.Client.Offline;

/// <summary>A change made on this device that the server hasn't got yet.</summary>
/// <param name="Kind">"create" (Fields is the whole entity), "update" (Fields are just the changed fields) or "delete".</param>
internal sealed record PendingChange(string Type, Guid Id, string Kind, JsonObject? Fields, DateTime QueuedAt);

/// <summary>
/// The member's data on this device, so the app works without the server. It's the last copy from
/// the server (the snapshot) plus the changes made here since (the outbox, in order). What the app
/// shows (the view) is the snapshot with the outbox applied on top. Kept per member in an
/// <see cref="ISettingsStore"/> (a file on desktop, local storage in the browser).
/// <para>
/// Entities are kept as the API's JSON (camelCase), so fields this client doesn't know survive.
/// Members are kept as public profiles only (no password hashes). Thread-safe: the view is replaced
/// as a whole, never changed in place, so reads need no lock.
/// </para>
/// </summary>
internal sealed class EntityCache
{
    /// <summary>The API's entity types (each at api/{type}), in the order they're fetched.</summary>
    public static readonly string[] Types = ["group", "member", "membership", "songbook", "song", "setlist", "setlistitem", "playlog", "play"];

    private static readonly string[] MemberProfileFields = ["id", "userName", "name", "joinedGroupId", "createdAt", "changedAt", "removed"];

    private readonly object gate = new();
    private readonly ISettingsStore store;
    private readonly string key;
    private readonly Dictionary<string, Dictionary<Guid, JsonObject>> snapshot = NewSet();
    private readonly List<PendingChange> outbox = [];
    private Dictionary<string, Dictionary<Guid, JsonObject>> view = NewSet();

    private EntityCache(ISettingsStore store, Guid memberId)
    {
        this.store = store;
        MemberId = memberId;
        key = $"cache.{memberId}";
    }

    public Guid MemberId { get; }

    /// <summary>When the snapshot was last fetched from the server; null if never on this device.</summary>
    public DateTime? LastSynced { get; private set; }

    public int PendingCount
    {
        get
        {
            lock (gate)
            {
                return outbox.Count;
            }
        }
    }

    /// <summary>The member's cache as saved on this device, or an empty one.</summary>
    public static EntityCache Load(ISettingsStore store, Guid memberId)
    {
        var cache = new EntityCache(store, memberId);
        try
        {
            if (store.Get(cache.key) is { } json && JsonNode.Parse(json) is JsonObject document)
            {
                cache.Read(document);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            // A damaged cache is no worse than none: the next sync fills it again.
            System.Diagnostics.Debug.WriteLine($"Ignoring the damaged offline cache: {ex.Message}");
        }

        cache.Rebuild();
        return cache;
    }

    /// <summary>The entities of a type as the app should show them (removed ones included; callers filter).</summary>
    public IEnumerable<JsonObject> All(string type) => view[type].Values;

    public JsonObject? Find(string type, Guid id) => view[type].GetValueOrDefault(id);

    public static bool IsRemoved(JsonObject entity) => entity["removed"] is JsonValue removed && removed.TryGetValue<bool>(out var value) && value;

    public static Guid IdOf(JsonObject entity) => Guid.Parse((string)entity["id"]!);

    /// <summary>Records a new entity (the whole of it).</summary>
    public void Create(string type, JsonObject entity) => Enqueue(new PendingChange(type, IdOf(entity), "create", (JsonObject)entity.DeepClone(), DateTime.Now));

    /// <summary>Records a change to some fields of an entity.</summary>
    public void Update(string type, Guid id, JsonObject fields) => Enqueue(new PendingChange(type, id, "update", (JsonObject)fields.DeepClone(), DateTime.Now));

    public void Delete(string type, Guid id) => Enqueue(new PendingChange(type, id, "delete", null, DateTime.Now));

    /// <summary>The oldest change the server hasn't got, if any.</summary>
    public PendingChange? Peek()
    {
        lock (gate)
        {
            return outbox.FirstOrDefault();
        }
    }

    /// <summary>The server has the change: forget it, keeping the server's stored copy if there is one.</summary>
    public void Complete(PendingChange change, JsonObject? stored)
    {
        lock (gate)
        {
            outbox.Remove(change);
            if (stored != null)
            {
                Put(snapshot, change.Type, stored);
            }
            else if (change.Kind == "delete" && snapshot[change.Type].TryGetValue(change.Id, out var existing))
            {
                existing["removed"] = true;
            }

            Rebuild();
            Save();
        }
    }

    /// <summary>The server refused the change for good: forget it (the server's copy stays).</summary>
    public void Drop(PendingChange change)
    {
        lock (gate)
        {
            outbox.Remove(change);
            Rebuild();
            Save();
        }
    }

    /// <summary>A full copy from the server replaces the snapshot; the changes not yet sent stay on top.</summary>
    public void ReplaceSnapshot(IReadOnlyDictionary<string, List<JsonObject>> fromServer)
    {
        lock (gate)
        {
            foreach (var (type, entities) in fromServer)
            {
                snapshot[type].Clear();
                foreach (var entity in entities)
                {
                    Put(snapshot, type, entity);
                }
            }

            LastSynced = DateTime.Now;
            Rebuild();
            Save();
        }
    }

    /// <summary>Changes pushed by the server (live updates) go into the snapshot; returns them as now shown.</summary>
    public IReadOnlyList<JsonObject> Merge(string type, IEnumerable<JsonObject> fromServer)
    {
        lock (gate)
        {
            var ids = new List<Guid>();
            foreach (var entity in fromServer)
            {
                Put(snapshot, type, entity);
                ids.Add(IdOf(entity));
            }

            Rebuild();
            Save();
            return ids.Select(id => view[type].GetValueOrDefault(id)).OfType<JsonObject>().ToArray();
        }
    }

    private void Enqueue(PendingChange change)
    {
        lock (gate)
        {
            outbox.Add(change);
            Rebuild();
            Save();
        }
    }

    private static void Put(Dictionary<string, Dictionary<Guid, JsonObject>> set, string type, JsonObject entity)
    {
        var copy = type == "member" ? Profile(entity) : (JsonObject)entity.DeepClone();
        set[type][IdOf(copy)] = copy;
    }

    /// <summary>A member as a public profile: Identity fields (password hash etc.) aren't kept on the device.</summary>
    private static JsonObject Profile(JsonObject member)
    {
        var profile = new JsonObject();
        foreach (var field in MemberProfileFields)
        {
            if (member.TryGetPropertyValue(field, out var value))
            {
                profile[field] = value?.DeepClone();
            }
        }

        return profile;
    }

    /// <summary>The view: the snapshot with the outbox applied in order. Built anew and swapped in.</summary>
    private void Rebuild()
    {
        var next = NewSet();
        foreach (var (type, entities) in snapshot)
        {
            foreach (var (id, entity) in entities)
            {
                next[type][id] = (JsonObject)entity.DeepClone();
            }
        }

        foreach (var change in outbox)
        {
            var entities = next[change.Type];
            switch (change.Kind)
            {
                case "create" when change.Fields != null:
                    entities[change.Id] = (JsonObject)change.Fields.DeepClone();
                    break;
                case "update" when change.Fields != null && entities.TryGetValue(change.Id, out var existing):
                    foreach (var (field, value) in change.Fields)
                    {
                        existing[field] = value?.DeepClone();
                    }

                    break;
                case "delete" when entities.TryGetValue(change.Id, out var deleted):
                    deleted["removed"] = true;
                    break;
            }
        }

        view = next;
    }

    private void Save()
    {
        var snapshotJson = new JsonObject();
        foreach (var (type, entities) in snapshot)
        {
            snapshotJson[type] = new JsonArray(entities.Values.Select(entity => (JsonNode)entity.DeepClone()).ToArray());
        }

        var outboxJson = new JsonArray(outbox.Select(change => (JsonNode)new JsonObject
        {
            ["type"] = change.Type,
            ["id"] = change.Id.ToString(),
            ["kind"] = change.Kind,
            ["fields"] = change.Fields?.DeepClone(),
            ["queuedAt"] = change.QueuedAt.ToString("O"),
        }).ToArray());

        var document = new JsonObject
        {
            ["lastSynced"] = LastSynced?.ToString("O"),
            ["snapshot"] = snapshotJson,
            ["outbox"] = outboxJson,
        };
        store.Set(key, document.ToJsonString());
    }

    private void Read(JsonObject document)
    {
        if (DateTime.TryParse((string?)document["lastSynced"], null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastSynced))
        {
            LastSynced = lastSynced;
        }

        if (document["snapshot"] is JsonObject snapshotJson)
        {
            foreach (var type in Types)
            {
                if (snapshotJson[type] is JsonArray entities)
                {
                    foreach (var entity in entities.OfType<JsonObject>())
                    {
                        Put(snapshot, type, entity);
                    }
                }
            }
        }

        if (document["outbox"] is JsonArray outboxJson)
        {
            foreach (var change in outboxJson.OfType<JsonObject>())
            {
                var type = (string)change["type"]!;
                if (Types.Contains(type))
                {
                    outbox.Add(new PendingChange(
                        type,
                        Guid.Parse((string)change["id"]!),
                        (string)change["kind"]!,
                        change["fields"]?.DeepClone() as JsonObject,
                        DateTime.Parse((string)change["queuedAt"]!, null, System.Globalization.DateTimeStyles.RoundtripKind)));
                }
            }
        }
    }

    private static Dictionary<string, Dictionary<Guid, JsonObject>> NewSet() => Types.ToDictionary(type => type, _ => new Dictionary<Guid, JsonObject>());
}
