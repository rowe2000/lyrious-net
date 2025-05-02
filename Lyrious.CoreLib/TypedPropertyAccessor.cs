using System.Reflection;

namespace Lyrious.CoreLib;

/// <summary>
/// Binds the get/set value methods of a PropertyInfo object to delegates to improve performance when accessing properties dynamically.
/// </summary>
/// <typeparam name="TTarget">The type of the object whose property that should be accessed.</typeparam>
/// <typeparam name="TValue">The type of the property.</typeparam>
public class TypedPropertyAccessor<TTarget, TValue> : PropertyAccessor<TTarget>
{
	private readonly Func<TTarget, TValue> _getter;
	private readonly Action<TTarget, TValue> _setter;

	public TypedPropertyAccessor(PropertyInfo pInfo) : this(pInfo, false)
	{
	}

	public TypedPropertyAccessor(PropertyInfo pInfo, bool nonPublic)
	{
		_propertyInfo = pInfo;

		// Bind get method if available
		var getMethodInfo = pInfo.GetGetMethod(nonPublic);
		if (getMethodInfo != null)
		{
			_getter = (Func<TTarget, TValue>)Delegate.CreateDelegate(typeof(Func<TTarget, TValue>), getMethodInfo);
		}

		// Bind set method if available
		var setMethodInfo = pInfo.GetSetMethod(nonPublic);
		if (setMethodInfo != null)
		{
			_setter = (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), setMethodInfo);
		}
	}

	/// <summary>
	/// Returns the property value of a specified object.
	/// </summary>
	/// <param name="obj">The object whose property value will be returned.</param>
	/// <returns></returns>
	public TValue GetValue(TTarget obj)
	{
		if (_getter == null)
		{
			throw new InvalidOperationException($"No getter implemented for {typeof(TTarget).Name}.{_propertyInfo.Name}");
		}

		return _getter(obj);
	}

	/// <summary>
	/// Sets the property value of a specified object.
	/// </summary>
	/// <param name="obj">The object whose property value will be set.</param>
	/// <param name="value">The new property value.</param>
	public void SetValue(TTarget obj, TValue value)
	{
		if (_setter == null)
		{
			throw new InvalidOperationException($"No setter implemented for {typeof(TTarget).Name}.{_propertyInfo.Name}");
		}

		_setter(obj, value);
	}

	/// <summary>
	/// Returns the property value of a specified object.
	/// </summary>
	/// <param name="obj">The object whose property value will be returned.</param>
	/// <returns></returns>
	public override object Get(TTarget obj)
	{
		if (_getter == null)
		{
			throw new InvalidOperationException($"No getter implemented for {typeof(TTarget).Name}.{_propertyInfo.Name}");
		}

		return _getter(obj);
	}

	/// <summary>
	/// Sets the property value of a specified object.
	/// </summary>
	/// <param name="obj">The object whose property value will be set.</param>
	/// <param name="value">The new property value.</param>
	public override void Set(TTarget obj, object value)
	{
		if (_setter == null)
		{
			throw new InvalidOperationException($"No setter implemented for {typeof(TTarget).Name}.{_propertyInfo.Name}");
		}

		_setter(obj, (TValue)value);
	}

	public override object Get(object obj)
	{
		return Get((TTarget)obj);
	}

	public override void Set(object obj, object value)
	{
		Set((TTarget)obj, value);
	}


	/// <summary>
	/// Returns a typed property accessor if propertyType matches and property is annotated with the specified attribute.
	/// </summary>
	/// <typeparam name="TAttribute"></typeparam>
	/// <param name="pInfo"></param>
	/// <returns></returns>
	public static TypedPropertyAccessor<TTarget, TValue> GetAccessorWithAttribute<TAttribute>(PropertyInfo pInfo) where TAttribute : Attribute
	{
		var attribute = pInfo.PropertyType == typeof(TValue) ? pInfo.GetCustomAttributes(typeof(TAttribute), false).FirstOrDefault() : null;
		if (attribute == null)
		{
			return null;
		}

		return new TypedPropertyAccessor<TTarget, TValue>(pInfo);
	}
}