using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

public class MemberController(LyriousRepository repo) : Controller<Member>(repo)
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
}
