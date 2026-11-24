namespace Lyrious.CoreLib.ApiModels;

public class RegisterModel
{
	public required string UserName { get; set; }
	public required string Password { get; set; }
	public required string Name { get; set; }
	public required string? Email { get; set; }
	public required string? PhoneNumber { get; set; }

	/// <summary>
	/// The code emailed to <see cref="Email"/> (see api/auth/register/send-code), or texted to
	/// <see cref="PhoneNumber"/> when registering with that instead (api/auth/register/send-sms-code),
	/// which proves it's the member's.
	/// </summary>
	public string? Code { get; set; }

	/// <summary>The new member's language ("sv" or "en"), kept as their language setting: the app's, and their emails' and texts'.</summary>
	public string? Language { get; set; }

	public static RegisterModel Create(string userName, string? password, string name, string? email, string? phoneNumber)
	{
		return new RegisterModel { UserName = userName, Password = password ?? "ASDqwe123_", Name = name, Email = email, PhoneNumber = phoneNumber };
	}
}