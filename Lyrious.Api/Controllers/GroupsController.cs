using Lyrious.CoreLib.Models;
using Lyrious.DataAccessLayer;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Lyrious.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GroupsController : CachedController<Group, LyriousContext>;

[ApiController]
[Route("api/[controller]")]
public class MembersController : CachedController<Member, LyriousContext>;

[ApiController]
[Route("api/[controller]")]
public class SetlistsController : CachedController<Setlist, LyriousContext>;

[ApiController]
[Route("api/[controller]")]
public class SongsController : CachedController<Song, LyriousContext>;