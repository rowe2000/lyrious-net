using System.Net.Mail;
using System.Text.Json.Nodes;
using Lyrious.Api.Hubs;
using Lyrious.Api.Services;
using Lyrious.CoreLib;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Lyrious.Api.Controllers;

public class MemberController(LyriousRepository repo, IEmailSender emailSender, IHubContext<LyriousHub> hub, HubConnectionRegistry registry) : Controller<Member>(repo)
{
	// GET api/member/{id}/settings
	/// <summary>A member's own settings, for a site manager (see LyriousRepository.SiteManagers): a JSON object; 204 if none.</summary>
	[HttpGet("{id:guid}/settings")]
	public ActionResult<JsonObject> GetSettingsOf(Guid id)
	{
		Repo.SetIdentity(User.Identity);
		if (!Repo.IsSiteManager)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only site managers see other members' settings.");
		}

		if (Repo.MemberForSiteManager(id) is not { } member)
		{
			return NotFound();
		}

		return string.IsNullOrEmpty(member.Settings) ? NoContent() : JsonNode.Parse(member.Settings) as JsonObject ?? [];
	}

	// PUT api/member/{id}/settings
	/// <summary>Replaces a member's own settings, as a site manager; sent to that member's connected devices.</summary>
	[HttpPut("{id:guid}/settings")]
	public async Task<IActionResult> PutSettingsOf(Guid id, [FromBody] JsonObject settings)
	{
		Repo.SetIdentity(User.Identity);
		if (!Repo.IsSiteManager)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only site managers change other members' settings.");
		}

		var json = settings.ToJsonString();
		if (json.Length > Member.MaxSettingsLength)
		{
			return BadRequest($"Settings may be at most {Member.MaxSettingsLength} characters.");
		}

		if (!Repo.SaveSettingsOf(id, json))
		{
			return NotFound();
		}

		// The member's devices take the change at once, as when they change it themselves.
		var connections = registry.GetConnections(id.ToString()).ToList();
		if (connections.Count > 0)
		{
			await hub.Clients.Clients(connections).SendAsync(LyriousHub.SettingsChangedMethod, json);
		}

		return NoContent();
	}

	// GET api/member/disabled
	/// <summary>The ids of the members a site manager has disabled, for a site manager.</summary>
	[HttpGet("disabled")]
	public ActionResult<IEnumerable<Guid>> GetDisabled()
	{
		Repo.SetIdentity(User.Identity);
		if (!Repo.IsSiteManager)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only site managers see which members are disabled.");
		}

		return Ok(Repo.DisabledMembersForSiteManager());
	}

	// PUT api/member/{id}/disabled
	/// <summary>
	/// Disables a member's account (true: they can't log in or use it, also with a token they have)
	/// or enables it again (false), as a site manager. Not their own account.
	/// </summary>
	[HttpPut("{id:guid}/disabled")]
	public IActionResult PutDisabled(Guid id, [FromBody] bool disabled)
	{
		Repo.SetIdentity(User.Identity);
		if (!Repo.IsSiteManager)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only site managers disable members.");
		}

		if (id == Repo.Me?.Id)
		{
			return BadRequest("You can't disable your own account.");
		}

		return Repo.SetMemberDisabled(id, disabled) ? NoContent() : NotFound();
	}

	// GET api/member/me/settings
	/// <summary>The caller's own settings (the same on all their devices): a JSON object; 204 No Content if they've never saved any.</summary>
	[HttpGet("me/settings")]
	public ActionResult<JsonObject> GetMySettings()
	{
		Repo.SetIdentity(User.Identity);
		if (Repo.Me is not { } me)
		{
			return Unauthorized();
		}

		if (string.IsNullOrEmpty(me.Settings))
		{
			return NoContent();
		}

		return JsonNode.Parse(me.Settings) as JsonObject ?? [];
	}

	// PUT api/member/me/settings
	/// <summary>
	/// Replaces the caller's own settings with the JSON object sent, and sends it to their other
	/// connected devices ("SettingsChanged"), not the one that sent it.
	/// </summary>
	[HttpPut("me/settings")]
	public async Task<IActionResult> PutMySettings([FromBody] JsonObject settings)
	{
		Repo.SetIdentity(User.Identity);
		if (Repo.Me is not { } me)
		{
			return Unauthorized();
		}

		var json = settings.ToJsonString();
		if (json.Length > Member.MaxSettingsLength)
		{
			return BadRequest($"Settings may be at most {Member.MaxSettingsLength} characters.");
		}

		// "{}" (not null) once saved, so all settings back to their defaults differs from none saved yet.
		Repo.SaveMySettings(json);
		var sender = Request.Headers[ChangeNotifier.ConnectionIdHeader].FirstOrDefault();
		var others = registry.GetConnections(me.Id.ToString()).Where(id => id != sender).ToList();
		if (others.Count > 0)
		{
			await hub.Clients.Clients(others).SendAsync(LyriousHub.SettingsChangedMethod, json);
		}

		return NoContent();
	}

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

		// In the inviter's language: the best guess for someone who has no account (and no language) yet.
		var language = AccountTexts.LanguageOf(Repo.Me);
		var inviter = !string.IsNullOrWhiteSpace(Repo.Me?.Name) ? Repo.Me.Name : Repo.Me?.UserName ?? AccountTexts.T(language, "A band admin");
		// Where the caller reached the API is where the web app is, too.
		var appUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";
		await emailSender.SendAsync(
			email,
			AccountTexts.T(language, "{0} invites you to {1} on Lyrious", inviter, group.Name),
			AccountTexts.T(language, "Hi,\n\n{0} invites you to the band {1} on Lyrious, where the band keeps its setlists and lyrics.\n\nTo join, create an account at {2} with this email address ({3}), and tell {0} that you have: they can then add you to the band.\n\nIf you don't know who this is from, you can ignore this email.", inviter, group.Name, appUrl, email),
			cancellationToken);
		return Ok();
	}
}
