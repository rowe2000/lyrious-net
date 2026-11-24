namespace Lyrious.CoreLib.ApiModels;

/// <summary>A new member's account, made by a band's admin, who then invites them to the band.</summary>
public class CreateMemberModel
{
	/// <summary>The band they're made for; the caller must be one of its admins.</summary>
	public required Guid GroupId { get; set; }

	/// <summary>The username they'll log in with.</summary>
	public required string UserName { get; set; }

	/// <summary>Their name, as the band sees it.</summary>
	public required string Name { get; set; }

	/// <summary>Their email address: where they're told about the account and set their password from.</summary>
	public required string Email { get; set; }
}
