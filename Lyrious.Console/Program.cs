// See https://aka.ms/new-console-template for more information

using Lyrious.CoreLib;
using Lyrious.CoreLib.Models;
using Lyrious.DataAccessLayer;

internal class Program
{
	private static void Main(string[] args)
	{
		Console.WriteLine("Hello, World!");

		//var lyriousContext = new LyriousContext(DbContextType.Sqllite);
		//await lyriousContext.Database.EnsureCreatedAsync();

		LyriousContext.CreateTestData();

		var LyriousService = new LyriousService();
		var songsRepo = new Repository<Song, LyriousContext>(new LyriousContext());

		var songs = songsRepo.GetAll();

		if (songs != null)
		{
			foreach (var song in songs)
			{
				Console.WriteLine($"Song: {song.Name}");
			}
		}
		else
		{
			Console.WriteLine("No songs found.");
		}

		Console.ReadLine();
		Console.WriteLine("Bye, World!");
	}
}
