using System.ComponentModel;

namespace Lyrious.CoreLib.Enums;

public enum AccidentalEnum
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