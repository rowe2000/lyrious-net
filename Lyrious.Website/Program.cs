namespace Lyrious.Website;

/// <summary>
/// The product website by itself, for working on it: its files (wwwroot) at http://localhost:5190,
/// the page at the root. On www.lyrious.se it's Lyrious.Api that serves these files, with the web app
/// under /app/ (its "Log in" links lead there, so they don't work here).
/// </summary>
public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		builder.WebHost.UseUrls("http://localhost:5190");
		var app = builder.Build();

		// Always checked with the server (ETag), so a change shows at once.
		app.UseDefaultFiles();
		app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" });
		app.Run();
	}
}
