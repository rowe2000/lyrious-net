namespace Lyrious.CoreLib.ApiModels;

public class ForgotPasswordModel
{
	/// <summary>The member's username or email address.</summary>
	public required string UserNameOrEmail { get; set; }
}

public class ResetPasswordModel
{
	/// <summary>The member's username or email address, as used when requesting the code.</summary>
	public required string UserNameOrEmail { get; set; }
	public required string Code { get; set; }
	public required string NewPassword { get; set; }
}
