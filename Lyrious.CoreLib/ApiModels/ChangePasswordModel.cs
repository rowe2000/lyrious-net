namespace Lyrious.CoreLib.ApiModels;

public class ChangePasswordModel
{
	public required string CurrentPassword { get; set; }
	public required string NewPassword { get; set; }

	/// <summary>The code emailed to the member's address (see api/auth/change-password/send-code).</summary>
	public string? Code { get; set; }
}
