namespace Lyrious.CoreLib.ApiModels;

public class EmailInvitationModel
{
	/// <summary>The band they're invited to; the caller must be one of its admins.</summary>
	public required Guid GroupId { get; set; }

	/// <summary>The email address of someone who isn't a member of Lyrious yet.</summary>
	public required string Email { get; set; }
}
