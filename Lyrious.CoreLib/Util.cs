using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using Lyrious.CoreLib.Commands;

namespace Lyrious.CoreLib;

public static class StringExtensions
{
	public static string ToSnakeCase(this string str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return str;
		}

		return string.Concat(str.Select((x, i) =>
			i > 0 && char.IsUpper(x) ? "_" + x.ToString().ToLowerInvariant() : x.ToString().ToLowerInvariant()));
	}
	
	public static string ToCamelCase(this string str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return str;
		}

		return char.ToLowerInvariant(str[0]) + str.Substring(1);
	}

	public static bool IsEmail(this string str)
	{
		return str?.Contains('@') == true;
	}
	public static bool IsPhoneNumber(this string str)
	{
		return true;
	}
}
public static class Util
{
	// Concurrent: CopyProperties is called from parallel API requests.
	private static readonly ConcurrentDictionary<Type, object> _dataAttributePropertiesDict = new();

	public static bool CopyProperties<T>(T source, T target)
	{
		if (!_dataAttributePropertiesDict.TryGetValue(typeof(T), out var accessorsList))
		{
			var properties = typeof(T).GetProperties()
				.Where(o => o.GetCustomAttributes(typeof(DataMemberAttribute), true).Any() || o.GetCustomAttributes(typeof(CopyAttribute), true).Any());
			accessorsList = properties.Select(PropertyAccessor<T>.CreateAccessor).ToList();
			_dataAttributePropertiesDict[typeof(T)] = accessorsList;
		}

		var changed = false;
		var typedAccessors = (List<PropertyAccessor<T>>)accessorsList;
		foreach (var accessor in typedAccessors)
		{
			var initialValue = accessor.Get(target);
			var newValue = accessor.Get(source);
			if (!Equals(initialValue, newValue))
			{
				changed = true;
				accessor.Set(target, newValue);
			}
		}

		return changed;
	}

	public static string GetPropertyChanges<T>(T current, T updated)
	{
		if (current == null || updated == null)
		{
			return null;
		}

		if (!_dataAttributePropertiesDict.TryGetValue(typeof(T), out var accessorsList))
		{
			var properties = typeof(T).GetProperties().Where(o => o.GetCustomAttributes(typeof(DataMemberAttribute), true).Any());
			accessorsList = properties.Select(PropertyAccessor<T>.CreateAccessor).ToList();
			_dataAttributePropertiesDict[typeof(T)] = accessorsList;
		}

		var typedAccessors = (List<PropertyAccessor<T>>)accessorsList;
		var changes = new List<string>();
		foreach (var accessor in typedAccessors)
		{
			var currentValue = accessor.Get(current);
			var updatedValue = accessor.Get(updated);
			if (!Equals(updatedValue, currentValue))
			{
				if (accessor.Type == typeof(string))
				{
					var updatedStrVal = $"\"{(updatedValue as string)?.Replace("\"", "")}\"";
					var currentStrVal = $"\"{(currentValue as string)?.Replace("\"", "")}\"";
					changes.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", accessor.Name, updatedStrVal, currentStrVal));
				}
				else if (accessor.Type == typeof(byte[]))
				{
					var updatedByteCount = ((updatedValue as byte[])?.Length) ?? -1;
					var currentByteCount = ((currentValue as byte[])?.Length) ?? -1;
					changes.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", accessor.Name, updatedByteCount, currentByteCount));
				}
				else
				{
					changes.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", accessor.Name, updatedValue, currentValue));
				}
			}
		}

		return changes.Any() ? string.Join(" ", changes.ToArray()) : null;
	}

	static Util()
	{
		_typesByAssembly = [];

		var assembly = Assembly.GetAssembly(typeof(Command));
		AddAssemblyTypes(assembly);

	}

	public static T CreateInstance<T>()
	{
		return CreateInstance<T>(null);
	}

	public static Type[] FindClasses<T>()
	{
		return GetTypes([typeof(T)]);
	}

	public static T CreateInstance<T>(Type baseType)
	{
		var baseTypes = baseType == null ? new[] { typeof(T) } : new[] { typeof(T), baseType };
		var type = FindClass(baseTypes)
		           ?? throw new NotImplementedException($"{typeof(T)} has no or multiple implementation");

		return (T)Activator.CreateInstance(type);
	}
	public static Type FindClass(IEnumerable<Type> types)
	{
		return GetTypes(types).OneOrDefault();
	}

	public static IEnumerable<Type> GetClassTypes()
	{
		return _typesByAssembly.Values.SelectMany(o => o);
	}

	private static IEnumerable<Type> GetTypes(Type baseType, string assemblyPrefix = "")
	{
		return AppDomain.CurrentDomain.GetAssemblies()
			.Where(assembly => assembly.FullName.StartsWith(assemblyPrefix))
			.SelectMany(assembly => assembly.GetTypes())
			.Where(type => type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type));
	}

	public static IEnumerable<Type> GetTypes<TBase>(string assemblyPrefix = "")
	{
		return GetTypes(typeof(TBase), assemblyPrefix);
	}

	private static Type[] GetTypes(IEnumerable<Type> searchTypes)
	{
		return _typesByAssembly.Values.SelectMany(o => o).Where(o => searchTypes.All(t => t == o || t.IsAssignableFrom(o))).ToArray();
	}

	private static readonly Dictionary<Assembly, Type[]> _typesByAssembly;

	private static void AddAssemblyTypes(Assembly assembly)
	{
		try
		{
			if (_typesByAssembly.ContainsKey(assembly))
			{
				return;
			}

			var types = assembly
				.GetTypes()
				.Where(o => o.IsClass && !o.IsAbstract)
				.ToArray();

			_typesByAssembly.Add(assembly, types);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			throw;
		}
	}
}
