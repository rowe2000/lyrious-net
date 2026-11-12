namespace Lyrious.CoreLib.ApiModels;

public class RegisterModel
{
	public required string UserName { get; set; }
	public required string Password { get; set; }
	public required string Name { get; set; }
	public required string? Email { get; set; }
	public required string? PhoneNumber { get; set; }

	public static RegisterModel Create(string userName, string? password, string name, string? email, string? phoneNumber)
	{
		return new RegisterModel { UserName = userName, Password = password ?? "ASDqwe123_", Name = name, Email = email, PhoneNumber = phoneNumber };
	}
}