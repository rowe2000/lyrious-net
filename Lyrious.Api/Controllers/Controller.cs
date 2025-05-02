using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860
namespace Lyrious.Api.Controllers;

[ApiController]
//[Authorize]
[Route("api/[controller]")]
public abstract class Controller<TEntity>(LyriousRepository repo) : ControllerBase
	where TEntity : class, IEntity, new()
{
	// GET: api/<TEntity>s
	[HttpGet]
	public virtual IEnumerable<TEntity> Get()
	{
		return repo.Get<TEntity>();
	}

	// GET api/<TEntity>s/<id>
	[HttpGet("{id:guid}")]
	public virtual TEntity Get(Guid id)
	{
		return repo.Get<TEntity>([id]).FirstOrDefault() ?? throw new KeyNotFoundException();
	}

	// POST api/<TEntity>s
	[HttpPost]
	public virtual async Task<IActionResult> Post([FromBody] IEnumerable<TEntity> entities)
	{
		repo.Update(entities);

		return Ok();
	}

	// PUT api/<TEntity>s/<id>
	[HttpPut("{id:guid}")]
	public virtual async Task<IActionResult> Put(Guid id, [FromBody] TEntity value)
	{
		repo.Update([value]);

		return Ok();
	}

	// DELETE api/<TEntity>/<id>
	[HttpDelete("{id:guid}")]
	public virtual async Task<IActionResult> Delete(Guid id)
	{
		repo.Remove<TEntity>([id]);

		return Ok();

	}
}