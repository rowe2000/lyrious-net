using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

public class MemberController(LyriousRepository repo) : Controller<Member>(repo)
{
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

		return new Member
		{
			Id = member.Id,
			UserName = member.UserName,
			Name = member.Name,
			CreatedAt = member.CreatedAt,
			ChangedAt = member.ChangedAt,
		};
	}
}
