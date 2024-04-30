using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Server.Controllers;

public abstract class CachedController<TEntity> : ControllerBase
    where TEntity : EntityBase, new()
{
    // GET: api/<TEntity>s
    [HttpGet]
    public virtual IEnumerable<TEntity> Get() => Cache.GetAll<TEntity>();

    // GET api/<TEntity>s/<id>
    [HttpGet("{id:guid}")]
    public virtual TEntity Get(Guid id) => Cache.Get<TEntity>(id) ?? throw new KeyNotFoundException();

    // POST api/<TEntity>s
    [HttpPost]
    public virtual void Post([FromBody] TEntity value) => Cache.Update([value]);

    // PUT api/<TEntity>s/<id>
    [HttpPut("{id:guid}")]
    public virtual void Put(Guid id, [FromBody] TEntity value) => Cache.Update([value]);

    // DELETE api/<TEntity>/<id>
    [HttpDelete("{id:guid}")]
    public virtual void Delete(Guid id) => Cache.Remove<TEntity>(id);
}