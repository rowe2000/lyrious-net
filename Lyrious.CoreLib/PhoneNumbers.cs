using System.Text.RegularExpressions;

namespace Lyrious.CoreLib;

/// <summary>
/// Phone numbers as people type them ("070-174 06 10", "+46 70 174 06 10", "0046…"), made into one
/// form (+46701740610) so the same number is always found and texted the same way.
/// </summary>
public static partial class PhoneNumbers
{
	/// <summary>
	/// The number in international form (+ and digits), or null when it isn't a phone number. A
	/// number starting with a single 0 is taken to be Swedish (070… is +4670…).
	/// </summary>
	public static string? Clean(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Contains('@'))
		{
			return null;
		}

		// Spaces, dashes, dots and brackets are only for reading.
		var number = Separators().Replace(value.Trim(), "");
		if (number.StartsWith("00", StringComparison.Ordinal))
		{
			number = "+" + number[2..];
		}
		else if (number.StartsWith('0'))
		{
			number = "+46" + number[1..];
		}

		return International().IsMatch(number) ? number : null;
	}

	[GeneratedRegex(@"[\s\-\.\(\)]")]
	private static partial Regex Separators();

	// + then a country code (not 0) and the rest: 8 to 15 digits in all.
	[GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
	private static partial Regex International();
}
