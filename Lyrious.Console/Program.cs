// See https://aka.ms/new-console-template for more information

using Lyrious.CoreLib;
using Lyrious.CoreLib.ApiModels;
using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;
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

		var repo = new LyriousRepository(new LyriousContext(optionsBuilder.Options, cache)) { RemoteAddress = "https://localhost:7079" };
		var commandHandler = CommandHandler.Create<Command>();

		Member? rw;
		rw = await repo.RegisterAsync(RegisterModel.Create("rowe", null, "Robert Westman", "robert@westman.st", "+46706343840"));
		var mb = await repo.RegisterAsync(RegisterModel.Create("mabe", null, "Maria Berggren", "", "+46706343840"));
		var tj = await repo.RegisterAsync(RegisterModel.Create("tojo", null, "Torbjörn Jonsson", "", "+46706343840"));
		var al = await repo.RegisterAsync(RegisterModel.Create("anlo", null, "Andreas Löfqvist", "", "+46706343840"));
		var tp = await repo.RegisterAsync(RegisterModel.Create("tope", null, "Tobias Pettersson", "", "+46706343840"));
		var rj = await repo.RegisterAsync(RegisterModel.Create("rojo", null, "Robert Jonsson", "", "+46706343840"));
		var hb = await repo.RegisterAsync(RegisterModel.Create("hebe", null, "Helena Bertholdsson", "", "+46706343840"));
		var mp = await repo.RegisterAsync(RegisterModel.Create("mapo", null, "Markus Porsklev", "", "+46706343840"));
		var mg = await repo.RegisterAsync(RegisterModel.Create("magy", null, "Mattias Gyllengahm", "", "+46706343840"));
		var ch = await repo.RegisterAsync(RegisterModel.Create("chri", null, "Chrille", "", ""));
		var fl = await repo.RegisterAsync(RegisterModel.Create("frlu", null, "Fredrik Lundqvist", "", ""));

		rw = await repo.LoginAsync(new LoginModel { Username = "rowe", Password = "ASDqwe123@£" });

		var jz = await repo.CreateGroupAsync("Jonzons");
		await repo.AddMemberAsync(jz, rw, RoleEnum.Admin);
		await repo.AddMemberAsync(jz, mb, RoleEnum.Conductor);
		await repo.AddMemberAsync(jz, tj);
		await repo.AddMemberAsync(jz, al);
		await repo.AddMemberAsync(jz, tp);

		await repo.JoinGroupAsync(jz, rw);
		await repo.JoinGroupAsync(jz, mb);
		await repo.JoinGroupAsync(jz, tj);
		await repo.JoinGroupAsync(jz, al);

		var song1 = await repo.AddSongAsync(jz.Songbooks[0], "The Best", "F");
		var song2 = await repo.AddSongAsync(jz.Songbooks[0], "Burning Love", "G");
		var song3 = await repo.AddSongAsync(jz.Songbooks[0], "Bye Bye Jonny", "G#", 130);
		var song4 = await repo.AddSongAsync(jz.Songbooks[0], "Back in black", "Em", 140);
		var song5 = await repo.AddSongAsync(jz.Songbooks[0], "Blackbird", "D#m", 150);

		var play = await repo.PlayAsync(jz, null, song2, rw, "C", 140);

		var ng = await repo.CreateGroupAsync("Norra Glädjen");
		var um = await repo.CreateGroupAsync("Utmarken");
		var cc = await repo.CreateGroupAsync("Captain Crew");

		await repo.AddMemberAsync(ng, rw, RoleEnum.Admin);
		await repo.AddMemberAsync(ng, rj, RoleEnum.Conductor);
		await repo.AddMemberAsync(ng, hb);
		await repo.AddMemberAsync(ng, mp);

		await repo.AddMemberAsync(um, mg, RoleEnum.Admin);
		await repo.AddMemberAsync(um, rj, RoleEnum.Conductor);
		await repo.AddMemberAsync(um, ch);

		await repo.AddMemberAsync(cc, fl, RoleEnum.Admin);
		await repo.AddMemberAsync(cc, tp);

		var setlist = await repo.CreateSetListAsync("Replista -25", jz);
		await repo.AddSongsAsync(setlist, [song1, song2, song3, song4, song5]);

		await commandHandler.Loop(repo);
	}
}