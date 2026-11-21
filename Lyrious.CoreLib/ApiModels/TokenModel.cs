using Lyrious.CoreLib.Models;

namespace Lyrious.CoreLib.ApiModels;

public class TokenModel
{
	public string Token { get; set; }
	public string Timeout { get; set; }
	public Member Member { get; set; }

	/// <summary>Whether the member is a site manager (see LyriousRepository.SiteManagers): admin of every band.</summary>
	public bool IsSiteManager { get; set; }
}