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

    public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
    {
	    foreach (var item in items)
	    {
		    action(item);
	    }
    }

    public static T OneOrDefault<T>(this IEnumerable<T> items)
    {
	    return items.OneOrDefault(o => true);
    }

    public static T OneOrDefault<T>(this IEnumerable<T> items, Func<T, bool> predicate)
    {
	    if (items == null)
	    {
		    return default;
	    }

	    var result = default(T);
	    var count = 0;

	    foreach (var item in items.Where(predicate))
	    {
		    if (count == 1)
		    {
			    return default;
		    }

		    result = item;
		    count++;
	    }

	    return result;
    }


}