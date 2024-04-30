namespace Lyrious.CoreLib.Exceptions;

public class InstanceExistsException<T>(string message) : Exception
{
    public Type Type => typeof(T);

    public override string Message { get; } = message;
}