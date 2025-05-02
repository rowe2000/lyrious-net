using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

namespace Lyrious.CoreLib;

public static class Util
{
	private static readonly Dictionary<Type, object> _dataAttributePropertiesDict = new Dictionary<Type, object>();

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

public abstract class Command()
{
	protected static readonly LyriousRepository Repo;

	protected virtual string[] ArgDescriptions { get; } = [];

	public abstract Task Execute(string[] args);

	public virtual void ThrowIfArgumentNotValid(string[] args)
	{
		if (args.Length > ArgDescriptions.Length)
		{
			return;
		}

		Console.WriteLine($"Usage: {GetType().Name} {string.Join(" ", ArgDescriptions)}");
		throw new ArgumentException();
	}
}