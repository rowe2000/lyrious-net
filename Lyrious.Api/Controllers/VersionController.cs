using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Lyrious.Api.Controllers;

/// <summary>
/// api/version: which version of the server this is, as its build recorded it (see
/// Directory.Build.props): the version (e.g. "0.1.14.0"), the git branch and the commit. Shown on the
/// app's Site manager page; nothing secret, so anyone may ask.
/// </summary>
[ApiController]
[Route("api/version")]
public class VersionController : ControllerBase
{
	// GET api/version
	[HttpGet]
	public IActionResult Get()
	{
		var assembly = typeof(VersionController).Assembly;
		// "0.1.14.0+main.1ca7dc1": the version, then the branch and the commit.
		var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
		var plus = informational.IndexOf('+');
		var version = plus >= 0 ? informational[..plus] : informational.Length > 0 ? informational : assembly.GetName().Version?.ToString() ?? "";
		var revision = plus >= 0 ? informational[(plus + 1)..] : "";
		var commit = revision.Contains('.') ? revision[(revision.LastIndexOf('.') + 1)..] : null;
		var branch = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == "Branch")?.Value;
		return Ok(new { version, branch, commit });
	}
}
