namespace Lyrious.CoreLib.Enums;

public enum Accidental
{
    Flat = -1,
    None = 0,
    Sharp = 1,
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