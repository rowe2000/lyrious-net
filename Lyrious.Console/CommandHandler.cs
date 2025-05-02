using Lyrious.CoreLib;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Models;

namespace Lyrious.ConsoleApp;

internal class CommandHandler
{
	private readonly Dictionary<string, Type> Commands = [];

	private async Task Execute(params string[] args)
	{
		Commands.TryGetValue(args[0], out var commandType);
		if (commandType == null)
		{
			throw new ArgumentNullException();
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
			Commands.Add(type.Name, type);
		}
	}

	public async Task Loop(LyriousRepository repo)
	{
		this.repo = repo;
		while (true)
		{
			var commandArgs = Console.ReadLine()?.Split(" ");
			switch (commandArgs?.FirstOrDefault())
			{
				case "quit":
				case "exit":
					Console.WriteLine("Bye Lyrious!");
					return;

				case "ping":
					break;

				default:
					await Execute(commandArgs);
					break;

				case null:
				case "":
					break;
			}
		}
	}
}

public class Template : Command
{
	protected override string[] ArgDescriptions => ["not implemented"];
	public override async Task Execute(string[] args)
	{
	}
}

public class AddSong : Command
{
	protected override string[] ArgDescriptions => ["<songname>", "<key>", "[<groupname>]"];

	public override async Task Execute(string[] args)
	{
		var group = Repo.Me?.JoinedGroup;
		if (args.Length>3)
		{
			group = Repo.Get<Group>(args[3]).FirstOrDefault();
		}

		await Repo.AddSongAsync(group?.Songbooks[0], args[1], args[2]);
	}
}
public class AddMember : Command
{
	protected override string[] ArgDescriptions => ["<groupname>", "[<username>]"];

	public override async Task Execute(string[] args)
	{
		var group = Repo.Get<Group>(args[1]).FirstOrDefault();
		var member = Repo.Get<Member>(args[2]).FirstOrDefault() ?? Repo.Me;
		if (group is null || member is null)
		{
			return;
		}
		
		await Repo.AddMemberAsync(group, member);
	}
}

public class CreateGroup : Command
{
	protected override string[] ArgDescriptions => ["<groupname>"];

	public override async Task Execute(string[] args)
	{
		var groupName = args[1];
		await Repo.CreateGroupAsync(groupName);
	}
}

public class Register : Command
{
	protected override string[] ArgDescriptions => ["<username>", "<password>", "<name>", "<email>", "<phone>"];

	public override async Task Execute(string[] args)
	{
		await Repo.RegisterAsync(RegisterModel.Create(args[1], args[2], args[3], args[4], args[5]));
	}
}

public class Login : Command
{
	protected override string[] ArgDescriptions => ["<username>", "<password>"];

	public override async Task Execute(string[] args)
	{
		await Repo.LoginAsync(LoginModel.Create(args[1], args[2]));
	}
}


public class Logout : Command
{
	public override async Task Execute(string[] args)
	{
		var result = await Repo.LogoutAsync();
	}
}


public class JoinGroup : Command
{
	protected override string[] ArgDescriptions { get; } = ["<groupname>", "[<username>]"];

	public override async Task Execute(string[] args)
	{
		var member = Repo.Get<Member>(args[2]).FirstOrDefault() ?? Repo.Me;
		var group = Repo.Get<Group>(args[1]).FirstOrDefault();
		if (group is null || member is null)
		{
			return;
		}
		
		await Repo.JoinGroupAsync(group, member);
	}
}

