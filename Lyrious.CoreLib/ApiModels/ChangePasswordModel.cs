namespace Lyrious.CoreLib.ApiModels;

public class ChangePasswordModel
{
	public required string CurrentPassword { get; set; }
	public required string NewPassword { get; set; }
}
