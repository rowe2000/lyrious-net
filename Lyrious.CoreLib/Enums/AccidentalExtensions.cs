namespace Lyrious.CoreLib.Enums;

public static class AccidentalExtensions
{
	public static string ToCustomString(this AccidentalEnum accidentalEnum)
	{
		return accidentalEnum switch
		{
			AccidentalEnum.Flat => "Flat",
			AccidentalEnum.None => "",
			AccidentalEnum.Sharp => "Sharp",
			_ => throw new ArgumentOutOfRangeException()
		};
	}
}