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

		var remoteAddress = args.ElementAtOrDefault(0) ?? "https://localhost:44354";
		var script = args.ElementAtOrDefault(1) ?? "populate.cmd";

		var cache = new Cache();
		var optionsBuilder = new DbContextOptionsBuilder<LyriousContext>();
		optionsBuilder.UseSqlite("Data Source=lyrious.db");

		var repo = new LyriousRepository(new LyriousContext(optionsBuilder.Options, cache)) { RemoteAddress = remoteAddress };
		Command.Repo = repo;
		var cmd = CommandHandler.Create<Command>();

		if (File.Exists(script))
		{
			await cmd.ExecuteScriptAsync(script);
		}

		await cmd.Loop();
	}
}
