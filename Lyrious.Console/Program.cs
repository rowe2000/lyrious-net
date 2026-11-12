using Lyrious.CoreLib;
using Lyrious.CoreLib.Commands;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.ConsoleApp;

internal class Program
{
	/// <param name="args">Optional: [0] API base address, [1] script to run before the prompt (default populate.cmd).</param>
	private static async Task Main(string[] args)
	{
		Console.WriteLine("Hello, Lyrious!");

		var remoteAddress = args.ElementAtOrDefault(0) ?? "http://localhost:7000";
		var script = args.ElementAtOrDefault(1) ?? "populate.cmd";

		var cache = new Cache();
		var optionsBuilder = new DbContextOptionsBuilder<LyriousContext>();
		// The local database is a copy of server data, filled one entity type at a time (and groups and
		// setlists reference each other), so foreign keys can't hold during a sync. The server enforces them.
		optionsBuilder.UseSqlite("Data Source=lyrious.db;Foreign Keys=False");

		var repo = new LyriousRepository(new LyriousContext(optionsBuilder.Options, cache)) { RemoteAddress = remoteAddress };
		repo.RemoteChanged += (entityType, entities) =>
			Console.WriteLine($"[live] {entityType.Name}: {string.Join("; ", entities.Select(o => o.ToString()))}");
		Command.Repo = repo;
		var cmd = CommandHandler.Create<Command>();

		if (File.Exists(script))
		{
			await cmd.ExecuteScriptAsync(script);
		}

		await cmd.Loop();
	}
}
