namespace Lyrious.CoreLib.ApiModels;

/// <summary>The password again, for an elevated (site manager) login (api/auth/elevate).</summary>
public class ElevateModel
{
	public required string Password { get; set; }
}
