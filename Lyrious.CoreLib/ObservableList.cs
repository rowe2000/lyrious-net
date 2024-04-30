using Lyrious.CoreLib.Enums;
using System.Collections;

namespace Lyrious.CoreLib;

public sealed class ObservableList<T> : IList<T>
{
    private readonly List<T> list = [];
    public event Action<ChangedArgs<T>>? Changed;

    public IEnumerator<T> GetEnumerator()
    {
        return list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void OnChanged(Changed changed, IEnumerable<T> items, int index = -1)
    {
        Changed?.Invoke(new ChangedArgs<T>(changed, items, index));
    }

    public void Add(T item)
    {
        Add([item]);
    }

    public void Add(IEnumerable<T> items)
    {
        var array = items.AsArray();
        list.AddRange(array);
        OnChanged(Enums.Changed.Add, array);
    }

    public void Clear()
    {
        var array = list.ToArray();
        list.Clear();
        OnChanged(Enums.Changed.Clear, array);
    }

    public bool Contains(T item)
    {
        return list.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        list.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        return Remove([item])[0];
    }

    public IList<bool> Remove(IEnumerable<T> items)
    {
        var removeSuccesses = new List<bool>();
        var removedItems = new List<T>();
        foreach (var item in items)
        {
            var isRemoved = list.Remove(item);
            removeSuccesses.Add(isRemoved);
            if (isRemoved)
            {
                removedItems.Add(item);
            }
        }

        OnChanged(Enums.Changed.Remove, removedItems);
        return removeSuccesses;
    }

    public void Move(int oldIndex, int newIndex)
    {
        var item = list[oldIndex];
        list.RemoveAt(oldIndex);
        list.Insert(newIndex, item);
        OnChanged(Enums.Changed.Move, [item], newIndex);
    }
    
    public int Count => list.Count;
    public bool IsReadOnly => false;

    public int IndexOf(T item)
    {
        return list.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        Insert(index, [item]);
    }

    public void Insert(int index, IEnumerable<T> items)
    {
        var array = items.AsArray();

        foreach (var item in array)
        {
            list.Insert(index++, item);
        }

        OnChanged(Enums.Changed.Insert, array, index);
    }

    public void RemoveAt(int index)
    {
        var item = list[index];
        list.RemoveAt(index);
        OnChanged(Enums.Changed.Remove, [item], index);
    }

    public T this[int index]
    {
        get => list[index];
        set
        {
            list[index] = value;
            OnChanged(Enums.Changed.Update, [value], index);
        }
    }
}