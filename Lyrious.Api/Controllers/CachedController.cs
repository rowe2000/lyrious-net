using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.Api.Controllers;

public class CachedController<TEntity, TContext> : ControllerBase
    where TEntity : EntityBase, new() 
    where TContext : DbContext, new()
{
    // GET: api/<TEntity>s
    [HttpGet]
    public virtual IEnumerable<TEntity> Get() => Cache.GetAll<TEntity>();

    // GET api/<TEntity>s/<id>
    [HttpGet("{id}")]
    public virtual TEntity Get(Guid id) => Cache.Get<TEntity>(id) ?? throw new KeyNotFoundException();

    // POST api/<TEntity>s
    [HttpPost]
    public virtual void Post([FromBody] TEntity value) => Cache.Update([value]);

    //// PUT api/<TEntity>s/<id>
    //[HttpPut("{id}")]
    //public virtual void Put(Guid id, [FromBody] TEntity value) => Cache.Update(id, value);

    // DELETE api/<TEntity>/<id>
    [HttpDelete("{id}")]
    public virtual void Delete(Guid id) => Cache.Remove<TEntity>(id);
}