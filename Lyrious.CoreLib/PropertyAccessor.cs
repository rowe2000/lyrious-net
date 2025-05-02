using System.Reflection;

namespace Lyrious.CoreLib;

public abstract class PropertyAccessor
{
	protected PropertyInfo _propertyInfo;

	public Type DeclaringType => _propertyInfo?.DeclaringType;
	public string Name => _propertyInfo?.Name;
	public Type Type => _propertyInfo?.PropertyType;
	public bool IsValueType => _propertyInfo?.PropertyType?.IsValueType == true;

	/// <summary>
	/// Returns the property value of a specified object.
	/// </summary>
	/// <param name="obj">The object whose property value will be returned.</param>
	/// <returns></returns>
	public abstract object Get(object obj);

	/// <summary>
	/// Sets the property value of a specified object.
	/// </summary>
	/// <param name="obj">The object whose property value will be set.</param>
	/// <param name="value">The new property value.</param>
	public abstract void Set(object obj, object value);

	/// <summary>
	/// Generates an PropertyAccessor object for optimized accessing of a property.
	/// </summary>
	/// <param name="instanceType"></param>
	/// <param name="pInfo">The property info of the property.</param>
	/// <returns></returns>
	public static PropertyAccessor CreateAccessor(Type instanceType, PropertyInfo pInfo)
	{
		return (PropertyAccessor)Activator.CreateInstance(typeof(TypedPropertyAccessor<,>).MakeGenericType(instanceType, pInfo.PropertyType), pInfo);
	}

	/// <summary>
	/// Generates an PropertyAccessor object for optimized accessing of a property.
	/// </summary>
	/// <param name="instanceType"></param>
	/// <param name="pInfo">The property info of the property.</param>
	/// <returns></returns>
	public static PropertyAccessor CreateAccessor(Type instanceType, PropertyInfo pInfo, bool nonPublic)
	{
		return (PropertyAccessor)Activator.CreateInstance(typeof(TypedPropertyAccessor<,>).MakeGenericType(instanceType, pInfo.PropertyType), pInfo, nonPublic);
	}

	/// <summary>
	/// Generates an PropertyAccessor object for optimized accessing of a property.
	/// </summary>
	/// <param name="instanceType"></param>
	/// <param name="propertyName">The name of the property.</param>
	/// <returns></returns>
	public static PropertyAccessor CreateAccessor(Type instanceType, string propertyName)
	{
		var pInfo = instanceType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return CreateAccessor(instanceType, pInfo);
	}

	public override string ToString()
	{
		return $"{_propertyInfo.Name}: {_propertyInfo.PropertyType.Name} [{_propertyInfo.DeclaringType.Name}]";
	}
}