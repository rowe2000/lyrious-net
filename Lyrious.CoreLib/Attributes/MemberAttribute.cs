namespace Lyrious.CoreLib.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class MemberAttribute : Attribute
{
}