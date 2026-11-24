using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lyrious.UILib;

namespace Lyrious.Client.Offline;

/// <summary>A change made on this device that the server hasn't got yet.</summary>
/// <param name="Kind">"create" (Fields is the whole entity), "update" (Fields are just the changed fields) or "delete".</param>
internal sealed record PendingChange(string Type, Guid Id, string Kind, JsonObject? Fields, DateTime QueuedAt);

/// <summary>
/// The member's data on this device, so the app works without the server. It's the last copy from
/// the server (the snapshot) plus the changes made here since (the outbox, in order). What the app
/// shows (the view) is the snapshot with the outbox applied on top. Kept per member in an
/// <see cref="ISettingsStore"/> (a file on desktop, IndexedDB in the browser).
/// <para>
/// Entities are kept as the API's JSON (camelCase), so fields this client doesn't know survive.
/// Members are kept as public profiles only (no password hashes). Thread-safe: the view is replaced
/// as a whole, never changed in place, so reads need no lock.
/// </para>
/// <para>
/// A change to a few entities rebuilds just those in the view, and the cache is saved to the store
/// in the background right after, so a change made here goes to the server (and one from the server
/// shows) without waiting for the whole cache to be copied and written.
/// </para>
/// <para>
/// Several instances of the app can share the store (browser tabs): each has the cache in memory
/// and saves the whole of it, so they pass their changes to each other to stay the same (see
/// <see cref="IInstanceLink"/>).
/// </para>
/// </summary>
internal sealed class EntityCache
{
    /// <summary>The API's entity types (each at api/{type}), in the order they're fetched.</summary>
    public static readonly string[] Types = ["group", "member", "membership", "songbook", "song", "setlist", "setlistitem", "playlog", "play", "songsetting"];

    private static readonly string[] MemberProfileFields = ["id", "userName", "name", "joinedGroupId", "lastLoginAt", "createdAt", "changedAt", "removed"];

    private readonly object gate = new();
    private readonly object saving = new();
    private bool savePending;
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
    public void Create(string type, JsonObject entity) => Enqueue(new PendingChange(type, IdOf(entity), "create", (JsonObject)entity.DeepClone(), Stamp()));

    /// <summary>Records a change to some fields of an entity.</summary>
    public void Update(string type, Guid id, JsonObject fields) => Enqueue(new PendingChange(type, id, "update", (JsonObject)fields.DeepClone(), Stamp()));

    public void Delete(string type, Guid id) => Enqueue(new PendingChange(type, id, "delete", null, Stamp()));

    // When a change was queued, never the same twice: with its entity and kind it names the
    // change between instances (KeyOf), and the clock can stand still between two changes.
    private static long lastStamp;

    private static DateTime Stamp()
    {
        while (true)
        {
            var last = System.Threading.Interlocked.Read(ref lastStamp);
            var next = Math.Max(DateTime.Now.Ticks, last + 1);
            if (System.Threading.Interlocked.CompareExchange(ref lastStamp, next, last) == last)
            {
                return new DateTime(next, DateTimeKind.Local);
            }
        }
    }

    /// <summary>The oldest change the server hasn't got, if any.</summary>
    public PendingChange? Peek()
    {
        lock (gate)
        {
            return outbox.FirstOrDefault();
        }
    }

    /// <summary>
    /// The server has the change: forget it, keeping the server's stored copy if there is one, else
    /// the change as sent (so what was made here stays shown until the server sends its copy).
    /// </summary>
    public void Complete(PendingChange change, JsonObject? stored)
    {
        lock (gate)
        {
            outbox.Remove(change);
            snapshot[change.Type].TryGetValue(change.Id, out var existing);
            if (stored != null)
            {
                Put(snapshot, change.Type, stored);
            }
            // The server may not show it yet: a new band, say, until the creator's membership (sent
            // next) arrives. Dropping it here would hide it until the next full download.
            else if (change.Kind == "create" && change.Fields != null)
            {
                Put(snapshot, change.Type, (JsonObject)change.Fields.DeepClone());
            }
            else if (change.Kind == "update" && change.Fields != null && existing != null)
            {
                foreach (var (field, value) in change.Fields)
                {
                    existing[field] = value?.DeepClone();
                }
            }
            else if (change.Kind == "delete" && existing != null)
            {
                existing["removed"] = true;
            }

            Rebuild(change.Type, [change.Id]);
            ScheduleSave();
        }
    }

    /// <summary>The server refused the change for good: forget it (the server's copy stays).</summary>
    public void Drop(PendingChange change)
    {
        lock (gate)
        {
            outbox.Remove(change);
            Rebuild(change.Type, [change.Id]);
            ScheduleSave();
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
            ScheduleSave();
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

            Rebuild(type, ids);
            ScheduleSave();
            return ids.Select(id => view[type].GetValueOrDefault(id)).OfType<JsonObject>().ToArray();
        }
    }

    /// <summary>
    /// A change to some fields of an entity that the server announced before storing it (it sends
    /// the stored entity soon after): goes into the snapshot like a live update. Returns the entity
    /// as now shown; null when it isn't in the cache.
    /// </summary>
    public JsonObject? MergeFields(string type, Guid id, JsonObject fields)
    {
        lock (gate)
        {
            if (!snapshot[type].TryGetValue(id, out var stored))
            {
                return null;
            }

            foreach (var (field, value) in fields)
            {
                stored[field] = value?.DeepClone();
            }

            Rebuild(type, [id]);
            ScheduleSave();
            return view[type].GetValueOrDefault(id);
        }
    }

    private void Enqueue(PendingChange change)
    {
        lock (gate)
        {
            outbox.Add(change);
            Rebuild(change.Type, [change.Id]);
            ScheduleSave();
        }

        Enqueued?.Invoke(change);
    }

    // ---- Other instances of the app sharing this store (see IInstanceLink) ----
    // They each hold the cache in memory, so a change made in one is passed to the others, which
    // lay it on top of their copy the same way. One of them sends the changes to the server and
    // tells the rest how each went.

    /// <summary>Raised when a change is made here (not for one passed on by another instance).</summary>
    public event Action<PendingChange>? Enqueued;

    /// <summary>Names a change the same way in every instance.</summary>
    public static string KeyOf(PendingChange change)
        => $"{change.Type}/{change.Id:N}/{change.Kind}/{change.QueuedAt.ToUniversalTime().Ticks}";

    /// <summary>The changes the server hasn't got yet, oldest first.</summary>
    public IReadOnlyList<PendingChange> Pending
    {
        get
        {
            lock (gate)
            {
                return outbox.ToArray();
            }
        }
    }

    /// <summary>A change made in another instance: queued here too. False if it's here already.</summary>
    public bool AddFromInstance(PendingChange change)
    {
        lock (gate)
        {
            var key = KeyOf(change);
            if (outbox.Any(queued => KeyOf(queued) == key))
            {
                return false;
            }

            outbox.Add(change);
            Rebuild(change.Type, [change.Id]);
            ScheduleSave();
            return true;
        }
    }

    /// <summary>Another instance sent the change to the server: as <see cref="Complete"/>.</summary>
    public void CompleteFromInstance(string key, string type, Guid id, JsonObject? stored)
    {
        lock (gate)
        {
            if (outbox.FirstOrDefault(queued => KeyOf(queued) == key) is { } change)
            {
                Complete(change, stored);
            }
            else if (stored != null)
            {
                // Not queued here (any more): the server's copy is news all the same.
                Put(snapshot, type, stored);
                Rebuild(type, [id]);
                ScheduleSave();
            }
        }
    }

    /// <summary>The server refused the change another instance sent: as <see cref="Drop"/>.</summary>
    public void DropFromInstance(string key)
    {
        lock (gate)
        {
            if (outbox.FirstOrDefault(queued => KeyOf(queued) == key) is { } change)
            {
                Drop(change);
            }
        }
    }

    /// <summary>
    /// Takes over the queue of the instance that sends the changes (when this one has just started:
    /// the copy it loaded may be a little behind), keeping the changes made here that it hasn't
    /// heard of yet.
    /// </summary>
    public void AdoptPending(IReadOnlyList<PendingChange> theirs, IReadOnlySet<string> madeHere)
    {
        lock (gate)
        {
            var known = theirs.Select(KeyOf).ToHashSet();
            var mine = outbox.Where(change => madeHere.Contains(KeyOf(change)) && !known.Contains(KeyOf(change))).ToList();
            outbox.Clear();
            outbox.AddRange(theirs);
            outbox.AddRange(mine);
            Rebuild();
            ScheduleSave();
        }
    }

    /// <summary>A change as JSON, as it's saved and as it's passed between instances.</summary>
    public static JsonObject ToJson(PendingChange change) => new()
    {
        ["type"] = change.Type,
        ["id"] = change.Id.ToString(),
        ["kind"] = change.Kind,
        ["fields"] = change.Fields?.DeepClone(),
        ["queuedAt"] = change.QueuedAt.ToString("O"),
    };

    /// <summary>The change in the JSON; null when it's of a type this app doesn't know.</summary>
    public static PendingChange? FromJson(JsonObject change)
    {
        var type = (string)change["type"]!;
        return Types.Contains(type)
            ? new PendingChange(
                type,
                Guid.Parse((string)change["id"]!),
                (string)change["kind"]!,
                change["fields"]?.DeepClone() as JsonObject,
                DateTime.Parse((string)change["queuedAt"]!, null, System.Globalization.DateTimeStyles.RoundtripKind))
            : null;
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

    /// <summary>Rebuilds just these entities of the view (from the snapshot and the outbox).</summary>
    private void Rebuild(string type, IReadOnlyCollection<Guid> ids)
    {
        // A new view sharing the untouched entities: readers of the old one see no change.
        var next = new Dictionary<string, Dictionary<Guid, JsonObject>>(view);
        var entities = next[type] = new Dictionary<Guid, JsonObject>(view[type]);
        foreach (var id in ids)
        {
            var entity = snapshot[type].TryGetValue(id, out var stored) ? (JsonObject)stored.DeepClone() : null;
            foreach (var change in outbox)
            {
                if (change.Type != type || change.Id != id)
                {
                    continue;
                }

                switch (change.Kind)
                {
                    case "create" when change.Fields != null:
                        entity = (JsonObject)change.Fields.DeepClone();
                        break;
                    case "update" when change.Fields != null && entity != null:
                        foreach (var (field, value) in change.Fields)
                        {
                            entity[field] = value?.DeepClone();
                        }

                        break;
                    case "delete" when entity != null:
                        entity["removed"] = true;
                        break;
                }
            }

            if (entity == null)
            {
                entities.Remove(id);
            }
            else
            {
                entities[id] = entity;
            }
        }

        view = next;
    }

    /// <summary>
    /// Saves the cache soon, in the background (changes close together are saved once). Called
    /// under the gate.
    /// </summary>
    private void ScheduleSave()
    {
        if (savePending)
        {
            return;
        }

        savePending = true;
        _ = Task.Run(SaveNow);
    }

    private void SaveNow()
    {
        // One save at a time, in order; the cache is copied under the gate, written outside it.
        lock (saving)
        {
            string json;
            lock (gate)
            {
                savePending = false;
                json = Document();
            }

            try
            {
                store.Set(key, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't save the offline cache: {ex.Message}");
            }
        }
    }

    /// <summary>The cache as saved: written straight out, without copying the entities first.</summary>
    private string Document()
    {
        var buffer = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (LastSynced is { } lastSynced)
            {
                writer.WriteString("lastSynced", lastSynced.ToString("O"));
            }
            else
            {
                writer.WriteNull("lastSynced");
            }

            writer.WriteStartObject("snapshot");
            foreach (var (type, entities) in snapshot)
            {
                writer.WriteStartArray(type);
                foreach (var entity in entities.Values)
                {
                    entity.WriteTo(writer);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteStartArray("outbox");
            foreach (var change in outbox)
            {
                writer.WriteStartObject();
                writer.WriteString("type", change.Type);
                writer.WriteString("id", change.Id.ToString());
                writer.WriteString("kind", change.Kind);
                writer.WritePropertyName("fields");
                if (change.Fields is { } fields)
                {
                    fields.WriteTo(writer);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WriteString("queuedAt", change.QueuedAt.ToString("O"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
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
                if (FromJson(change) is { } queued)
                {
                    outbox.Add(queued);
                }
            }
        }
    }

    private static Dictionary<string, Dictionary<Guid, JsonObject>> NewSet() => Types.ToDictionary(type => type, _ => new Dictionary<Guid, JsonObject>());
}
