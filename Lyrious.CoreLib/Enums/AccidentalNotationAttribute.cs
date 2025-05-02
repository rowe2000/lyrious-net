namespace Lyrious.CoreLib.Enums;

[AttributeUsage(AttributeTargets.All)]
public class AccidentalNotationAttribute(string notation) : Attribute
{
    public string Notation { get; } = notation;
}