using Lyrious.Api.Services;
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
	public virtual async Task<IActionResult> Post([FromBody] IEnumerable<TEntity> entities)
	{
		repo.SetIdentity(User.Identity);
		var array = entities.ToArray();
		if (!array.All(repo.CanWrite))
		{
			return Forbidden();
		}

		repo.Update(array);
		await NotifyAsync(array.Select(o => o.Id));
		return Ok();
	}

	// PUT api/<TEntity>s/<id>
	[HttpPut("{id:guid}")]
	public virtual async Task<IActionResult> Put(Guid id, [FromBody] TEntity value)
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
		await NotifyAsync([id]);
		return Ok();
	}

	// DELETE api/<TEntity>/<id>
	[HttpDelete("{id:guid}")]
	public virtual async Task<IActionResult> Delete(Guid id)
	{
		repo.SetIdentity(User.Identity);
		if (!repo.CanRemove<TEntity>(id))
		{
			return NotFound();
		}

		// Removal is a soft delete, so clients are sent the entity with Removed = true.
		repo.Remove<TEntity>([id]);
		await NotifyAsync([id]);
		return Ok();
	}

	/// <summary>Pushes the stored versions of the written entities to the hub groups that may see them.</summary>
	private Task NotifyAsync(IEnumerable<Guid> ids)
	{
		var stored = repo.Get<TEntity>(ids).ToArray();
		var notifier = HttpContext.RequestServices.GetRequiredService<ChangeNotifier>();
		return notifier.NotifyAsync(stored, Request.Headers[ChangeNotifier.ConnectionIdHeader].FirstOrDefault());
	}

	// Plain 403 with a message rather than Forbid(), which returns an empty response.
	private ObjectResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, "Not allowed to modify this entity.");
}
