namespace Lyrious.CoreLib.ApiModels;

public class LoginModel
{
	public required string Username { get; set; }
	public required string Password { get; set; }

	public static LoginModel Create(string user, string password)
	{
		return new LoginModel
		{
			Username = user,
			Password = password,
		};
	}
}