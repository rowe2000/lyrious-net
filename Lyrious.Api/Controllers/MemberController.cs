using System.Net.Mail;
using Lyrious.Api.Services;
using Lyrious.CoreLib;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

public class MemberController(LyriousRepository repo, IEmailSender emailSender) : Controller<Member>(repo)
{
	// Other members' Identity fields (password hash, email, ...) are never sent.
	protected override Member ForCaller(Member entity) => entity.ToPublicProfile();

	// GET api/member/find?query=<username|email|phone>
	// Finds any member (not just those sharing a group) so they can be invited.
	// Only the public profile is returned.
	[HttpGet("find")]
	public async Task<ActionResult<Member>> Find([FromQuery] string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return BadRequest("Query is required.");
		}

		var member = await Repo.FindMemberAsync(query);
		if (member == null)
		{
			return NotFound();
		}

		return member.ToPublicProfile();
	}

	// POST api/member/invite-email
	/// <summary>
	/// Emails someone who isn't a member of Lyrious yet, asking them to create an account to join
	/// the band. It doesn't make them a member of the band: once they have an account, an admin
	/// invites them as any other member. The band's admins only.
	/// </summary>
	[HttpPost("invite-email")]
	public async Task<IActionResult> InviteByEmail([FromBody] EmailInvitationModel model, CancellationToken cancellationToken)
	{
		Repo.SetIdentity(User.Identity);
		if (!Repo.IsAdminOf(model.GroupId) || Repo.Get<Group>([model.GroupId]).FirstOrDefault() is not { } group)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only the band's admins can invite.");
		}

		var email = model.Email.Trim();
		if (!email.Contains('@') || !MailAddress.TryCreate(email, out var address) || address.Address != email)
		{
			return BadRequest("That isn't an email address.");
		}

		if (await Repo.FindMemberAsync(email) != null)
		{
			return Conflict("They have an account already: invite them as a member.");
		}

		if (!emailSender.CanSend)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "The server isn't set up to send email, so they can't be invited by email.");
		}

		var inviter = !string.IsNullOrWhiteSpace(Repo.Me?.Name) ? Repo.Me.Name : Repo.Me?.UserName ?? "A band admin";
		// Where the caller reached the API is where the web app is, too.
		var appUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";
		await emailSender.SendAsync(
			email,
			$"{inviter} invites you to {group.Name} on Lyrious",
			$"Hi,\n\n{inviter} invites you to the band {group.Name} on Lyrious, where the band keeps its setlists and lyrics.\n\n"
			+ $"To join, create an account at {appUrl} with this email address ({email}), and tell {inviter} that you have: they can then add you to the band.\n\n"
			+ "If you don't know who this is from, you can ignore this email.",
			cancellationToken);
		return Ok();
	}
}
