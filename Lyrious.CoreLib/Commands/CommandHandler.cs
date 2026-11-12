using System.Text.RegularExpressions;

namespace Lyrious.CoreLib.Commands;

public class CommandHandler
{
	private readonly Dictionary<string, Type> Commands = [];

	private async Task Execute(string[] args)
	{
		Commands.TryGetValue(args[0].ToLower(), out var commandType);
		if (commandType == null)
		{
			Console.WriteLine($"No command as {args[0]}");
			return;
		}

		var command = Util.CreateInstance<Command>(commandType);
		command.ThrowIfArgumentNotValid(args);

		await command.Execute(args);
	}

	private LyriousRepository repo = null!;

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

	public CommandHandler()
	{
	}

	public void AddCommands(IEnumerable<Type> types)
	{
		foreach (var type in types)
		{
			Commands.Add(type.Name.ToLower(), type);
		}
	}

	public async Task Loop(LyriousRepository repo)
	{
		this.repo = repo;
		while (Execute(Console.ReadLine())) { }
	}

	public bool Execute(string? line)
	{
		if (line?.FirstOrDefault() == '#')
		{
			return true;
		}
		
		var commandArgs = Regex
			.Matches(line, @"[^\s""']+|""([^""]*)""|'([^']*)'")
			.Select(o => o.Value)
			.ToArray();

		switch (commandArgs.FirstOrDefault())
		{
			case "quit":
			case "exit":
				Console.WriteLine("Bye Lyrious!");
				return false;

			case "ping":
				break;

			default:
				try
				{
					_ = Execute(commandArgs);
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
				}
				break;

			case "?":
				Util.FindClasses<Command>().ForEach(o => Util.CreateInstance<Command>(o).PrintHelp());
				break;

			case null:
			case "":
				break;
		}

		return true;
	}

	public void ExecuteScript(string fileName)
	{
		var lines = File.ReadAllLines(fileName);
		lines.ForEach(line => Execute(line));
	}
}