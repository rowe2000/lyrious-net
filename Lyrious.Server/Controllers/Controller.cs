using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Lyrious.Server.Controllers;

public abstract class Controller<TEntity>(IRepository repo) : ControllerBase
	where TEntity : class, IEntity, new()
{
	// GET: api/<TEntity>s
	[HttpGet]
	public virtual IEnumerable<TEntity> Get() => repo.Get<TEntity>();

	// GET api/<TEntity>s/<id>
	[HttpGet("{id:guid}")]
	public virtual TEntity Get(Guid id) => repo.Get<TEntity>([id]).FirstOrDefault() ?? throw new KeyNotFoundException();

	// POST api/<TEntity>s
	[HttpPost]
	public virtual void Post([FromBody] TEntity value) => repo.Update([value]);

	// PUT api/<TEntity>s/<id>
	[HttpPut("{id:guid}")]
	public virtual void Put(Guid id, [FromBody] TEntity value) => repo.Update([value]);

	// DELETE api/<TEntity>/<id>
	[HttpDelete("{id:guid}")]
	public virtual void Delete(Guid id) => repo.Remove<TEntity>([id]);
}

[ApiController]
[Route("api/[controller]")]
public class GroupsController(IRepository repo) : Controller<Group>(repo);

[ApiController]
[Route("api/[controller]")]
public class MembersController(IRepository repo) : Controller<Member>(repo);

[ApiController]
[Route("api/[controller]")]
public class SetlistsController(IRepository repo) : Controller<Setlist>(repo);

[ApiController]
[Route("api/[controller]")]
public class SongsController(IRepository repo) : Controller<Song>(repo);