// See https://aka.ms/new-console-template for more information

using Lyrious.CoreLib;
using Lyrious.CoreLib.Commands;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.ConsoleApp;

internal class Program
{
	private static async Task Main(string[] args)
	{
		Console.WriteLine("Hello, Lyrious!");

		var cache = new Cache();
		var optionsBuilder = new DbContextOptionsBuilder<LyriousContext>();
		optionsBuilder.UseSqlite("Data Source=lyrious.db");

		var repo = new LyriousRepository(new LyriousContext(optionsBuilder.Options, cache)) { RemoteAddress = "https://localhost:44354" };
		Command.Repo = repo;
		var cmd = CommandHandler.Create<Command>();

		//var rw = await repo.LoginAsync("rowe", "ASDqwe123_");
		//if (rw == null)
		{
			cmd.ExecuteScript("populate.cmd");
			//await PopulateData(repo, cmd);
		}


		await cmd.Loop(repo);
	}

	private static async Task PopulateData(LyriousRepository repo, CommandHandler cmd)
	{
		cmd.Execute("Register rowe ASDqwe123_ \"Robert Westman\" robert@westman.st, 0706343840");
		cmd.Execute("Register tojo ASDqwe123_ \"Torbjörn Jonsson\" robert@westman.st, 0706343840");
		cmd.Execute("Register mabe ASDqwe123_ \"Maria Berggren\" robert@westman.st, 0706343840");
		cmd.Execute("Register anlo ASDqwe123_ \"Andreas Löfqvist\" robert@westman.st, 0706343840");
		cmd.Execute("Register tope ASDqwe123_ \"Tobias Pettersson\" robert@westman.st, 0706343840");
		cmd.Execute("Register rojo ASDqwe123_ \"Robert Jonsson\" robert@westman.st, 0706343840");
		cmd.Execute("Register hebe ASDqwe123_ \"Helena Bertholdsson\" robert@westman.st, 0706343840");
		cmd.Execute("Register mapo ASDqwe123_ \"Markus Porsklev\" robert@westman.st, 0706343840");
		cmd.Execute("Register magy ASDqwe123_ \"Mattias Gyllengahm\" robert@westman.st, 0706343840");
		cmd.Execute("Register chri ASDqwe123_ \"Chrille\" robert@westman.st, 0706343840");
		cmd.Execute("Register frlu ASDqwe123_ \"Fredrik Lundqvist\" robert@westman.st, 0706343840");

		//var rowe = await repo.RegisterAsync("rowe", null, "Robert Westman", "robert@westman.st", "+46706343840");
		//var mabe = await repo.RegisterAsync("mabe", null, "Maria Berggren", null, "+46706343840");
		//var tojo = await repo.RegisterAsync("tojo", null, "Torbjörn Jonsson", null, "+46706343840");
		//var anlo = await repo.RegisterAsync("anlo", null, "Andreas Löfqvist", null, "+46706343840");
		//var tope = await repo.RegisterAsync("tope", null, "Tobias Pettersson", null, "+46706343840");
		//var rojo = await repo.RegisterAsync("rojo", null, "Robert Jonsson", null, "+46706343840");
		//var hebe = await repo.RegisterAsync("hebe", null, "Helena Bertholdsson", null, "+46706343840");
		//var mapo = await repo.RegisterAsync("mapo", null, "Markus Porsklev", null, "+46706343840");
		//var magy = await repo.RegisterAsync("magy", null, "Mattias Gyllengahm", null, "+46706343840");
		//var chri = await repo.RegisterAsync("chri", null, "Chrille", null, "");
		//var frlu = await repo.RegisterAsync("frlu", null, "Fredrik Lundqvist", null, "");

		cmd.Execute("Login rowe ASDqwe123_");
		cmd.Execute("CreateGroup Jonzons");
		cmd.Execute("InviteMember mabe Conductor");
		cmd.Execute("InviteMember tojo");
		cmd.Execute("InviteMember anlo");
		cmd.Execute("InviteMember tope");

		//rowe = await repo.LoginAsync("rowe", "ASDqwe123_");
		//var jonzs = await repo.CreateGroupAsync("Jonzons");
		//await repo.InviteMemberAsync(mabe, RoleEnum.Conductor);
		//await repo.InviteMemberAsync(tojo);
		//await repo.InviteMemberAsync(anlo);
		//await repo.InviteMemberAsync(tope);

		cmd.Execute("CreateSong \"The Best\" F");
		cmd.Execute("CreateSong \"Burning Love\" G");
		cmd.Execute("CreateSong \"Bye Bye Jonny\" G#");
		cmd.Execute("CreateSong \"Back in black\" E");
		cmd.Execute("CreateSong \"Blackbird\" Em");

		//var song1 = await repo.CreateSongAsync("The Best", "F");
		//var song2 = await repo.CreateSongAsync("Burning Love", "G");
		//var song3 = await repo.CreateSongAsync("Bye Bye Jonny", "G#", 130);
		//var song4 = await repo.CreateSongAsync("Back in black", "Em", 140);
		//var song5 = await repo.CreateSongAsync("Blackbird", "D#m", 150);

		cmd.Execute("CreateSetlist \"Replista -25\"");

		//var replista = await repo.CreateSetListAsync("Replista -25");
		//		var setlistItem = await repo.AddSongsAsync(replista, [song1, song2, song3, song4, song5]);

		//	var play = await repo.PlayAsync(null, song2, "C", 140);

		cmd.Execute("CreateGroup \"Norra Glädjen\"");
		//var norra = await repo.CreateGroupAsync("Norra Glädjen");
		cmd.Execute("InviteMember rojo Conductor");
		cmd.Execute("InviteMember hebe");
		cmd.Execute("InviteMember mapo");
		//await repo.InviteMemberAsync(rojo, RoleEnum.Conductor);
		//await repo.InviteMemberAsync(hebe);
		//await repo.InviteMemberAsync(mapo);

		//await repo.LogoutAsync();

		//tojo = await repo.LoginAsync("tojo", "ASDqwe123_");
		//await repo.ApproveMembershipAsync(jonzs);
		//await repo.JoinGroupAsync(jonzs);
		//await repo.LogoutAsync();

		//anlo = await repo.LoginAsync("anlo", "ASDqwe123_");
		//await repo.ApproveMembershipAsync(jonzs);
		//await repo.JoinGroupAsync(jonzs);
		//await repo.LogoutAsync();

		//mabe = await repo.LoginAsync("mabe", "ASDqwe123_");
		//await repo.ApproveMembershipAsync(jonzs);
		//await repo.JoinGroupAsync(jonzs);
		//var play2 = await repo.PlayAsync(setlistItem.FirstOrDefault());
		//await repo.LogoutAsync();

		//await repo.ApproveMembershipAsync(norra);
		//await repo.ApproveMembershipAsync(norra);
		//await repo.ApproveMembershipAsync(norra);
		//await repo.ApproveMembershipAsync(norra);

		//magy = await repo.LoginAsync("magy", "ASDqwe123_");
		//var utmark = await repo.CreateGroupAsync("Utmarken");
		//await repo.InviteMemberAsync(magy, RoleEnum.Admin);
		//await repo.InviteMemberAsync(rojo, RoleEnum.Conductor);
		//await repo.InviteMemberAsync(chri);

		//var ccrew = await repo.CreateGroupAsync("Captain Crew");
		//await repo.InviteMemberAsync(frlu, RoleEnum.Admin);
		//await repo.InviteMemberAsync(tope);

		//var nsons = await repo.CreateGroupAsync("Native Sons");

	}
}