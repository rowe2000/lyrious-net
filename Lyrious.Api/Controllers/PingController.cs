using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

[ApiController]
[Route("api/ping")]
public class PingController : ControllerBase
{
	[HttpPost]
	public IActionResult Ping()
	{
		return Ok("Pong");
	}
}