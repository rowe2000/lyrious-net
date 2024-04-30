using System.Collections;
using System.Collections.Concurrent;

namespace Lyrious.CoreLib;

public class BiDictionary<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull where TValue : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> keyToValue;
    private readonly ConcurrentDictionary<TValue, TKey> valueToKey;

    public BiDictionary(IEnumerable<KeyValuePair<TKey, TValue>> dict)
    {
        var pairs = dict.AsArray();

        keyToValue = new ConcurrentDictionary<TKey, TValue>(1, pairs.Length);
        valueToKey = new ConcurrentDictionary<TValue, TKey>(1, pairs.Length);

        foreach (var pair in pairs)
        {
            keyToValue.TryAdd(pair.Key, pair.Value);
            valueToKey.TryAdd(pair.Value, pair.Key);
        }
    }

    public BiDictionary()
    {
        keyToValue = new ConcurrentDictionary<TKey, TValue>();
        valueToKey = new ConcurrentDictionary<TValue, TKey>();
    }

    public bool ContainsKey(TKey key)
    {
        return keyToValue.ContainsKey(key);
    }

    public bool ContainsValue(TValue value)
    {
        return valueToKey.ContainsKey(value);
    }

    public void Add(TKey key, TValue value)
    {
        keyToValue.TryAdd(key, value);
        valueToKey.TryAdd(value, key);
    }

    public bool Remove(TKey key)
    {
        return keyToValue.TryRemove(key, out var value) && valueToKey.TryRemove(value, out _);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        return keyToValue.TryGetValue(key, out value);
    }

    public bool TryGetKey(TValue value, out TKey key)
    {
        return valueToKey.TryGetValue(value, out key);
    }

    public TValue this[TKey key]
    {
        get => keyToValue[key];
        set
        {
            keyToValue[key] = value;
            valueToKey[value] = key;
        }
    }

    public ICollection<TKey> Keys => keyToValue.Keys;
    public ICollection<TValue> Values => valueToKey.Keys;

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return keyToValue.GetEnumerator();
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        keyToValue.TryAdd(item.Key, item.Value);
        valueToKey.TryAdd(item.Value, item.Key);
    }

    public void Clear()
    {
        keyToValue.Clear();
        valueToKey.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return keyToValue.Contains(item);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        throw new NotImplementedException(
            $"{nameof(BiDictionary<TKey, TValue>)}.{nameof(ICollection<KeyValuePair<TKey, TValue>>.CopyTo)} is not implemented");
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return keyToValue.TryRemove(item.Key, out _) && valueToKey.TryRemove(item.Value, out _);
    }

    public int Count => keyToValue.Count;
    public bool IsReadOnly => false;
}