namespace Lyrious.CoreLib.ApiModels;

/// <summary>The phone number a member wants a code texted to, to make it theirs.</summary>
public class PhoneCodeModel
{
	/// <summary>The number as typed (e.g. 070-174 06 10); it's cleaned (see <see cref="PhoneNumbers"/>).</summary>
	public required string PhoneNumber { get; set; }

	/// <summary>The language to text the code in ("sv" or "en"), when registering (the language chosen for the new account).</summary>
	public string? Language { get; set; }
}

/// <summary>A member's new phone number, with the code texted to it; an empty number removes theirs (no code needed).</summary>
public class ChangePhoneModel
{
	/// <summary>The number the code was texted to; empty to remove the member's number.</summary>
	public string? PhoneNumber { get; set; }

	/// <summary>The code texted to the number.</summary>
	public string? Code { get; set; }
}
