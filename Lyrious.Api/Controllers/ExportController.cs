using Lyrious.Api.Services;
using Lyrious.CoreLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

/// <summary>
/// api/export: a complete export of Lyrious' data (see <see cref="DataExport"/>), for site managers
/// logged in elevated only, as JSON (?format=json, the default) or as a SQLite database (?format=sqlite).
/// </summary>
[ApiController]
[Authorize]
[Route("api/export")]
public class ExportController(LyriousRepository repo, LyriousContext context) : ControllerBase
{
	// GET api/export?format=json|sqlite
	[HttpGet]
	public IActionResult Get([FromQuery] string? format = null)
	{
		repo.SetIdentity(User.Identity);
		if (!repo.IsSiteManager)
		{
			return StatusCode(StatusCodes.Status403Forbidden, "Only site managers export all data.");
		}

		var tables = DataExport.Tables(context);
		var stamp = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
		return string.Equals(format, "sqlite", StringComparison.OrdinalIgnoreCase)
			? File(DataExport.Sqlite(tables), "application/vnd.sqlite3", $"lyrious-export-{stamp}.db")
			: File(DataExport.Json(tables), "application/json", $"lyrious-export-{stamp}.json");
	}
}
