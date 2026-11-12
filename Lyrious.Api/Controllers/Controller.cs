using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860
namespace Lyrious.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public abstract class Controller<TEntity>(LyriousRepository repo) : ControllerBase
	where TEntity : class, IEntity, new()
{
	protected LyriousRepository Repo => repo;

	// GET: api/<TEntity>s
	[HttpGet]
	public virtual IEnumerable<TEntity> Get()
	{
		repo.SetIdentity(User.Identity);
		return repo.Get<TEntity>();
	}

	// GET api/<TEntity>s/<id>
	[HttpGet("{id:guid}")]
	public virtual ActionResult<TEntity> Get(Guid id)
	{
		repo.SetIdentity(User.Identity);
		var entity = repo.Get<TEntity>([id]).FirstOrDefault();

		// Entities the caller can't see are reported as missing rather than forbidden,
		// so ids from other groups can't be probed.
		return entity is null ? NotFound() : entity;
	}

	// POST api/<TEntity>s
	[HttpPost]
	public virtual IActionResult Post([FromBody] IEnumerable<TEntity> entities)
	{
		repo.SetIdentity(User.Identity);
		var array = entities.ToArray();
		if (!array.All(repo.CanWrite))
		{
			return Forbidden();
		}

		repo.Update(array);
		return Ok();
	}

	// PUT api/<TEntity>s/<id>
	[HttpPut("{id:guid}")]
	public virtual IActionResult Put(Guid id, [FromBody] TEntity value)
	{
		if (value.Id != id)
		{
			return BadRequest("Route id does not match entity id.");
		}

		repo.SetIdentity(User.Identity);
		if (!repo.CanWrite(value))
		{
			return Forbidden();
		}

		repo.Update([value]);
		return Ok();
	}

	// DELETE api/<TEntity>/<id>
	[HttpDelete("{id:guid}")]
	public virtual IActionResult Delete(Guid id)
	{
		repo.SetIdentity(User.Identity);
		if (!repo.CanRemove<TEntity>(id))
		{
			return NotFound();
		}

		repo.Remove<TEntity>([id]);
		return Ok();
	}

	// Plain 403: Forbid() would go through the Identity cookie handler and redirect to an HTML access-denied page.
	private ObjectResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, "Not allowed to modify this entity.");
}
