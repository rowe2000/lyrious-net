using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Lyrious.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GroupsController : CachedController<Group>;

[ApiController]
[Route("api/[controller]")]
public class MembersController : CachedController<Member>;

[ApiController]
[Route("api/[controller]")]
public class SetlistsController : CachedController<Setlist>;

[ApiController]
[Route("api/[controller]")]
public class SongsController : CachedController<Song>;