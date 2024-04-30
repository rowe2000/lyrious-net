using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib;

public class Key
{
    public NoteLetter NoteLetter { get; set; }
    public Accidental Accidental { get; set; }
    public bool Minor { get; set; }

    public int Octave { get; set; }

    public Key()
    {
    }

    public Key(string keyString)
    {
        Parse(keyString);
    }

    public void Parse(string value)
    {
        value = value.Trim();

        NoteLetter = value[0] switch
        {
            'A' => NoteLetter.A,
            'B' => NoteLetter.B,
            'C' => NoteLetter.C,
            'D' => NoteLetter.D,
            'E' => NoteLetter.E,
            'F' => NoteLetter.F,
            'G' => NoteLetter.G,
            _ => NoteLetter
        };

        Minor = value.Contains('m');

        Accidental = value.Contains('#') ? Accidental.Sharp : value.Contains('b') ? Accidental.Flat : Accidental.None;
    }

    public override string ToString()
    {
        return NoteLetter + Accidental.ToCustomString() + (Minor ? "m" : "");
    }
}