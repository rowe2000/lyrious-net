namespace Lyrious.CoreLib.ApiModels;

/// <summary>Asks for a verification code to be emailed to <see cref="Email"/> (to register with it, or change to it).</summary>
public class EmailCodeModel
{
	public required string Email { get; set; }

	/// <summary>The language to email the code in ("sv" or "en"), when registering (the language chosen for the new account).</summary>
	public string? Language { get; set; }
}

/// <summary>Changes the logged-in member's email address to <see cref="NewEmail"/>, with the code emailed there.</summary>
public class ChangeEmailModel
{
	public required string NewEmail { get; set; }
	public required string Code { get; set; }
}
