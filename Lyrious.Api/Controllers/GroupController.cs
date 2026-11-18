using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;

namespace Lyrious.Api.Controllers;

public class GroupController(LyriousRepository repo) : Controller<Group>(repo)
{
	// GET: api/group
	/// <summary>
	/// The caller's bands. Every sync starts here, so this is where a member gets their private band
	/// (also members from before there were private bands, and on a saved login).
	/// </summary>
	public override IEnumerable<Group> Get()
	{
		Repo.SetIdentity(User.Identity);
		Repo.EnsurePrivateGroup();
		return base.Get();
	}
}
