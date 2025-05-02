using Lyrious.CoreLib.Enums;

namespace Lyrious.CoreLib.Models;

public class Key
{
    public NoteLetter NoteLetter { get; set; }
    public AccidentalEnum AccidentalEnum { get; set; }
    public bool Minor { get; set; }
    public int Octave { get; set; }

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

        AccidentalEnum = value.Contains('#') ? AccidentalEnum.Sharp : value.Contains('b') ? AccidentalEnum.Flat : AccidentalEnum.None;
    }

    public override string ToString()
    {
        return NoteLetter + AccidentalEnum.ToCustomString() + (Minor ? "m" : "");
    }
}