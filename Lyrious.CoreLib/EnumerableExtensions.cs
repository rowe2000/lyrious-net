using System.Collections;

namespace Lyrious.CoreLib;

public static class EnumerableExtensions
{
    public static IList<T> AsIList<T>(this IEnumerable<T> items)
    {
        return items as IList<T> ?? items.ToArray();
    }

    public static T[] AsArray<T>(this IEnumerable<T> items)
    {
        return items as T[] ?? items.ToArray();
    }

    public static T[] ToMany<T>(T item)
    {
        return [item];
    }

    public static T EmptyIfNull<T>(this T l)
        where T : IEnumerable, new()
    {
        return l == null ? new T() : l;
    }

    public static T[] EmptyIfNull<T>(this T[]? l)
    {
        return l ?? [];
    }

    public static ICollection<T> AsCollection<T>(this IEnumerable<T> items)
    {
        return items as ICollection<T> ?? items?.ToArray();
    }
}