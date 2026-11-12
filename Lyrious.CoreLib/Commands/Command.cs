using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;
using Group = Lyrious.CoreLib.Models.Group;

namespace Lyrious.CoreLib.Commands;

public abstract class Command()
{
	public static LyriousRepository Repo { get; set; }

	protected virtual string[] ArgDescriptions { get; } = [];

	public abstract Task Execute(string[] args);

	public virtual void ThrowIfArgumentNotValid(string[] args)
	{
		if (args.Length > ArgDescriptions.Length)
		{
			return;
		}

		PrintHelp(args);
		throw new ArgumentException();
	}

	public void PrintHelp(string[]? args = null)
	{
		var join = args == null ? "" : "  : [" + string.Join(" ", args) + "]";
		Console.WriteLine($"Usage: {GetType().Name} {string.Join(" ", ArgDescriptions)}{join}");
	}
}

public class Template : Command
{
	protected override string[] ArgDescriptions => ["not implemented"];
	public override async Task Execute(string[] args)
	{
	}
}

public class PlayFromSetList : Command
{
	protected override string[] ArgDescriptions => ["<position>"];
	public override async Task Execute(string[] args)
	{
		var position = int.Parse(args[1]);
		var setlistSetlistItem = Repo.Me?.JoinedGroup?.CurrentSetlist?.SetlistItems[position];
		if (setlistSetlistItem?.Song == null)
		{
			return;
		}	
		
		
		//var setlistItem = Repo.Get<SetlistItem>(args[1]).FirstOrDefault();
		await Repo.PlayAsync(setlistSetlistItem);
	}
}

public class AddSong : Command
{
	protected override string[] ArgDescriptions => ["<songname>", "<key>"];

	public override async Task Execute(string[] args)
	{
		var group = Repo.Me?.JoinedGroup;
		if (args.Length>3)
		{
			group = Repo.Get<Group>(args[3]).FirstOrDefault();
		}

		await Repo.CreateSongAsync(group?.Songbooks[0], args[1], args[2]);
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
		
		await Repo.InviteMemberAsync(member);
	}
}

public class InviteMember : Command
{
	protected override string[] ArgDescriptions => ["<username>"];

	public override async Task Execute(string[] args)
	{
		Member? member;
		if (args[1].IsEmail())
		{
			member = Repo.Get<Member>(o => o.Email == args[1]).FirstOrDefault() ?? Repo.Me;
		}
		else if (args[1].IsPhoneNumber())
		{
			member = Repo.Get<Member>(o => o.PhoneNumber == args[1]).FirstOrDefault() ?? Repo.Me;
		}
		else
		{
			member = Repo.Get<Member>(o => o.UserName == args[1]).FirstOrDefault() ?? Repo.Me;
		}

		var role = RoleEnum.Member;
		if (args.Length <= 3)
		{
			Enum.TryParse(args[2], out role);
		}
		
		await Repo.InviteMemberAsync(member, role);
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
	protected override string[] ArgDescriptions => ["<username>", "<password>", "<name>"];

	public override async Task Execute(string[] args)
	{
		var ret = await Repo.RegisterAsync(args[1], args[2], args[3]); //, args[4], args[5]);
		Console.WriteLine(ret == null ? $"Member not created" : $"Member {ret.Name} was created");
	}
}

public class Login : Command
{
	protected override string[] ArgDescriptions => ["<username>", "<password>"];

	public override async Task Execute(string[] args)
	{
		await Repo.LoginAsync(args[1], args[2]);
	}
}


public class Logout : Command
{
	public override async Task Execute(string[] args)
	{
		await Repo.LogoutAsync();
	}
}


public class JoinGroup : Command
{
	protected override string[] ArgDescriptions { get; } = ["<groupname>"];

	public override async Task Execute(string[] args)
	{
		var member = Repo.Get<Member>(args[2]).FirstOrDefault() ?? Repo.Me;
		var group = Repo.Get<Group>(args[1]).FirstOrDefault();
		if (group is null || member is null)
		{
			return;
		}

		await Repo.JoinGroupAsync(group);
	}
}

public class ApproveMembership : Command
{
	protected override string[] ArgDescriptions { get; } = ["<groupname>"];

	public override async Task Execute(string[] args)
	{
		var member = Repo.Get<Member>(args[2]).FirstOrDefault() ?? Repo.Me;
		var group = Repo.Get<Group>(args[1]).FirstOrDefault();
		if (group is null || member is null)
		{
			return;
		}

		await Repo.ApproveMembershipAsync(group);
	}
}

