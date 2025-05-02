using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib;

public class ChangedArgs<T>(ChangedEnum changedEnum, IEnumerable<T> values, int index = -1)
{
    public int Index { get; } = -1;
    public ChangedEnum ChangedEnum { get; } = changedEnum;
    public T[] Values { get; } = values.AsArray();
}