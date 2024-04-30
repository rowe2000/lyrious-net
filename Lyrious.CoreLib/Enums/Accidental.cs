using System.ComponentModel;

namespace Lyrious.CoreLib.Enums;

public enum Accidental
{
    [Description("Flat")]
    [AccidentalNotation("b")]
    Flat = -1,
    [AccidentalNotation("")]
    [Description("")]
    None = 0,
    [AccidentalNotation("#")]
    [Description("Sharp")]
    Sharp = 1
}

[AttributeUsage(AttributeTargets.All)]
public class AccidentalNotationAttribute(string notation) : Attribute
{
    public string Notation { get; } = notation;
}

public static class AccidentalExtensions
{
    public static string ToCustomString(this Accidental accidental)
    {
        return accidental switch
        {
            Accidental.Flat => "Flat",
            Accidental.None => "",
            Accidental.Sharp => "Sharp",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}