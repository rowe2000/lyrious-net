using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib.ApiModels;

public class TokenModel
{
	public string Token { get; set; }
	public string Timeout { get; set; }
	public Member Member { get; set; }

	/// <summary>Whether the token is an elevated login: site-manager rights (admin of every band).</summary>
	public bool IsSiteManager { get; set; }

	/// <summary>Whether the member is listed as a site manager, so may log in elevated (api/auth/elevate).</summary>
	public bool CanElevate { get; set; }
}