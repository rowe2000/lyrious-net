using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib;

public class ChangedArgs<T>(Changed changed, IEnumerable<T> values, int index = -1)
{
    public int Index { get; } = -1;
    public Changed Changed { get; } = changed;
    public T[] Values { get; } = values.AsArray();
}