using System.Text.RegularExpressions;

namespace Lyrious.CoreLib.Commands;

public class CommandHandler
{
	// A bare word, or a "double" / 'single' quoted string whose quotes are stripped.
	private static readonly Regex ArgumentPattern = new(@"""(?<arg>[^""]*)""|'(?<arg>[^']*)'|(?<arg>[^\s""']+)");

	private readonly Dictionary<string, Type> Commands = new(StringComparer.OrdinalIgnoreCase);

	public static CommandHandler Create(IEnumerable<Type> types)
	{
		var commandHandler = new CommandHandler();
		commandHandler.AddCommands(types);
		return commandHandler;
	}

	public static CommandHandler Create<T>(string assemblyPrefix = "")
	{
		var commandHandler = new CommandHandler();
		commandHandler.AddCommands(Util.GetTypes<T>(assemblyPrefix));
		return commandHandler;
	}

	public void AddCommands(IEnumerable<Type> types)
	{
		foreach (var type in types)
		{
			Commands.Add(type.Name, type);
		}
	}

	/// <summary>Reads and executes commands from the console until quit/exit or end of input.</summary>
	public async Task Loop()
	{
		while (await ExecuteAsync(Console.ReadLine()))
		{
		}
	}

	/// <summary>Executes the script line by line, waiting for each command to finish before the next.</summary>
	public async Task ExecuteScriptAsync(string fileName)
	{
		foreach (var line in await File.ReadAllLinesAsync(fileName))
		{
			if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
			{
				Console.WriteLine($"> {line.Trim()}");
			}

			if (!await ExecuteAsync(line))
			{
				break;
			}
		}
	}

	/// <summary>
	/// Executes one command line. Returns false when the session should end (quit/exit or end of input).
	/// Command failures are reported and don't end the session.
	/// </summary>
	public async Task<bool> ExecuteAsync(string? line)
	{
		if (line == null)
		{
			return false;
		}

		var args = ParseArguments(line);
		if (args.Length == 0 || args[0].StartsWith('#'))
		{
			return true;
		}

		switch (args[0].ToLowerInvariant())
		{
			case "quit":
			case "exit":
				Console.WriteLine("Bye Lyrious!");
				return false;

			case "ping":
				return true;

			case "?":
				foreach (var type in Commands.Values.OrderBy(o => o.Name))
				{
					Util.CreateInstance<Command>(type).PrintHelp();
				}

				return true;
		}

		if (!Commands.TryGetValue(args[0], out var commandType))
		{
			Console.WriteLine($"No command as {args[0]}");
			return true;
		}

		var command = Util.CreateInstance<Command>(commandType);
		if (!command.ValidateArguments(args))
		{
			return true;
		}

		try
		{
			await command.Execute(args);
		}
		catch (Exception e)
		{
			Console.WriteLine($"{commandType.Name} failed: {e.Message}");
		}

		return true;
	}

	public static string[] ParseArguments(string line)
	{
		return ArgumentPattern
			.Matches(line)
			.Select(o => o.Groups["arg"].Value)
			.ToArray();
	}
}
