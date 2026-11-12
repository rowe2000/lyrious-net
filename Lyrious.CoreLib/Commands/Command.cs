using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;
using Group = Lyrious.CoreLib.Models.Group;

namespace Lyrious.CoreLib.Commands;

public abstract class Command()
{
	public static LyriousRepository Repo { get; set; } = null!;

	/// <summary>Argument descriptions; required ones are written &lt;like-this&gt;, optional ones [&lt;like-this&gt;].</summary>
	protected virtual string[] ArgDescriptions { get; } = [];

	private int RequiredArgumentCount => ArgDescriptions.Count(o => !o.StartsWith('['));

	public abstract Task Execute(string[] args);

	/// <summary>Checks that all required arguments are present (args[0] is the command name); prints usage if not.</summary>
	public bool ValidateArguments(string[] args)
	{
		if (args.Length - 1 >= RequiredArgumentCount)
		{
			return true;
		}

		PrintHelp(args);
		return false;
	}

	public void PrintHelp(string[]? args = null)
	{
		var join = args == null ? "" : "  : [" + string.Join(" ", args) + "]";
		Console.WriteLine($"Usage: {GetType().Name} {string.Join(" ", ArgDescriptions)}{join}");
	}

	protected static Member RequireMe()
	{
		return Repo.Me ?? throw new InvalidOperationException("Not logged in.");
	}

	protected static Group RequireGroup(string? groupName = null)
	{
		if (groupName == null)
		{
			var joinedGroupId = RequireMe().JoinedGroupId ?? throw new InvalidOperationException("No joined group.");
			return Repo.Get<Group>([joinedGroupId]).FirstOrDefault()
			       ?? throw new InvalidOperationException("Joined group not found.");
		}

		return Repo.Get<Group>(groupName).FirstOrDefault()
		       ?? throw new InvalidOperationException($"Group '{groupName}' not found.");
	}

	protected static Setlist RequireSetlist(Group group, string setlistName)
	{
		return Repo.Get<Setlist>(o => o.GroupId == group.Id && o.Name == setlistName).FirstOrDefault()
		       ?? throw new InvalidOperationException($"Setlist '{setlistName}' not found in {group.Name}.");
	}
}

public class PlayFromSetlist : Command
{
	protected override string[] ArgDescriptions => ["<setlistname>", "<position>"];

	public override async Task Execute(string[] args)
	{
		var setlist = RequireSetlist(RequireGroup(), args[1]);
		var position = int.Parse(args[2]);

		var setlistItem = Repo.Get<SetlistItem>(o => o.SetlistId == setlist.Id && o.Position == position && !o.Removed).FirstOrDefault()
		                  ?? throw new InvalidOperationException($"No song at position {position} in {setlist.Name}.");
		var song = Repo.Get<Song>([setlistItem.SongId]).FirstOrDefault()
		           ?? throw new InvalidOperationException("Song not found.");

		var play = await Repo.PlayAsync(setlistItem, song);
		Console.WriteLine(play == null ? "Not played (only the group's conductor can start a song)." : $"Playing {play}");
	}
}

public class CreateSong : Command
{
	protected override string[] ArgDescriptions => ["<songname>", "<key>", "[<groupname>]"];

	public override async Task Execute(string[] args)
	{
		var group = RequireGroup(args.ElementAtOrDefault(3));
		var songbook = Repo.Get<Songbook>(o => o.GroupId == group.Id).FirstOrDefault()
		               ?? throw new InvalidOperationException($"{group.Name} has no songbook.");

		var song = await Repo.CreateSongAsync(songbook, args[1], args[2]);
		Console.WriteLine(song == null ? "Song not created" : $"Song {song} created in {group.Name}");
	}
}

public class CreateSetlist : Command
{
	protected override string[] ArgDescriptions => ["<setlistname>"];

	public override async Task Execute(string[] args)
	{
		var setlist = await Repo.CreateSetListAsync(args[1], RequireGroup());
		Console.WriteLine($"Setlist {setlist} created");
	}
}

public class AddToSetlist : Command
{
	protected override string[] ArgDescriptions => ["<setlistname>", "<songname>", "[<songname>...]"];

	public override async Task Execute(string[] args)
	{
		var group = RequireGroup();
		var setlist = RequireSetlist(group, args[1]);
		var songbookIds = Repo.Get<Songbook>(o => o.GroupId == group.Id).Select(o => o.Id).ToHashSet();

		var songs = args.Skip(2)
			.Select(name => Repo.Get<Song>(o => o.Name == name && songbookIds.Contains(o.SongbookId)).FirstOrDefault()
			                ?? throw new InvalidOperationException($"Song '{name}' not found in {group.Name}."))
			.ToArray();

		var items = (await Repo.AddSongsAsync(setlist, songs)).ToArray();
		Console.WriteLine($"Added {items.Length} songs to {setlist.Name}");
	}
}

public class InviteMember : Command
{
	protected override string[] ArgDescriptions => ["<username|email|phone>", "[<role>]"];

	public override async Task Execute(string[] args)
	{
		var member = await Repo.FindMemberAsync(args[1])
		             ?? throw new InvalidOperationException($"No member '{args[1]}'.");

		var role = RoleEnum.Member;
		if (args.Length > 2 && !Enum.TryParse(args[2], true, out role))
		{
			throw new ArgumentException($"Unknown role '{args[2]}'. Use one of: {string.Join(", ", Enum.GetNames<RoleEnum>())}.");
		}

		var membership = await Repo.InviteMemberAsync(member, role);
		Console.WriteLine(membership == null ? "Not invited (no joined group?)" : $"Invited {member.Name} as {role}");
	}
}

public class CreateGroup : Command
{
	protected override string[] ArgDescriptions => ["<groupname>"];

	public override async Task Execute(string[] args)
	{
		var group = await Repo.CreateGroupAsync(args[1]);
		Console.WriteLine(group == null ? "Group not created (not logged in?)" : $"Group {group.Name} created");
	}
}

public class Register : Command
{
	protected override string[] ArgDescriptions => ["<username>", "<password>", "<name>", "[<email>]", "[<phone>]"];

	public override async Task Execute(string[] args)
	{
		var ret = await Repo.RegisterAsync(args[1], args[2], args[3], args.ElementAtOrDefault(4), args.ElementAtOrDefault(5));
		Console.WriteLine(ret == null ? $"Member not created" : $"Member {ret.Name} was created");
	}
}

public class Login : Command
{
	protected override string[] ArgDescriptions => ["<username>", "<password>"];

	public override async Task Execute(string[] args)
	{
		var member = await Repo.LoginAsync(args[1], args[2]);
		Console.WriteLine(member == null ? "Login failed" : $"Logged in as {member.Name}");
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
		RequireMe();
		var group = RequireGroup(args[1]);
		var joined = await Repo.JoinGroupAsync(group);
		Console.WriteLine(joined ? $"Joined {group.Name}" : $"Not joined {group.Name} (already joined, or no approved membership)");
	}
}

public class ApproveMembership : Command
{
	protected override string[] ArgDescriptions { get; } = ["<groupname>"];

	public override async Task Execute(string[] args)
	{
		RequireMe();
		var group = RequireGroup(args[1]);
		var membership = await Repo.ApproveMembershipAsync(group);
		Console.WriteLine(membership == null ? $"No invitation to {group.Name}" : $"Membership in {group.Name} approved");
	}
}
