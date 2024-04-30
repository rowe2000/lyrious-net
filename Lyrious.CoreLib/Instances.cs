using Lyrious.CoreLib.Exceptions;

namespace Lyrious.CoreLib;

public static class Instances
{
    internal static class TypedInstances<T> where T : new()
    {
        private static bool exist;

        internal static T Create()
        {
            ThrowIfExists();
            exist = true;
            return new T();
        }


        internal static void ThrowIfExists()
        {
            if (exist)
            {
                throw new InstanceExistsException<T>($@"Instance of {nameof(T)} already exists.");
            }
        }

        internal static void Reset()
        {
            exist = false;
        }
    }


    public static T Create<T>() where T : new()
    {
        return TypedInstances<T>.Create();
    }

    public static void ThrowIfExists<T>(this T _) where T : new()
    {
        TypedInstances<T>.ThrowIfExists();
    }

    public static void ThrowIfExists<T>() where T : new()
    {
        TypedInstances<T>.ThrowIfExists();
    }

    public static void ResetInstance<T>(this T _) where T : new()
    {
        TypedInstances<T>.Reset();
    }

    public static void ResetInstance<T>() where T : new()
    {
        TypedInstances<T>.Reset();
    }
}