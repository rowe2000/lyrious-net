﻿// See https://aka.ms/new-console-template for more information

using Lyrious.CoreLib;
using Lyrious.CoreLib.Client;
using Lyrious.CoreLib.Models;
using Lyrious.DataAccessLayer;
using Lyrious.Server.Hubs;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net;

Console.WriteLine("Hello, World!");

//var lyriousContext = new LyriousContext(DbContextType.Sqllite);
//await lyriousContext.Database.EnsureCreatedAsync();




LyriousContext.CreateTestData();

var LyriousService = new LyriousService();


var songsRepo = new Repository<Song, LyriousContext>(new LyriousContext());

Console.ReadLine();
Console.WriteLine("Bye, World!");



public class LyriousService
{
    private readonly LyriousHub hub;
    private readonly HubClient client;

    public LyriousService()
    {
        hub = new LyriousHub();

        client = new HubClient("https://localhost:7181");
        client.Reconnected += Client_Reconnected;
        client.Closed += Client_Closed;
    }

    private void Client_Reconnected(object sender, string? connectionId)
    {
        client.BindCache<Group>();
        client.BindCache<Member>();
        client.BindCache<Membership>();
        client.BindCache<Play>();
        client.BindCache<Playlog>();
        client.BindCache<Setlist>();
        client.BindCache<SetlistItem>();
        client.BindCache<Song>();
        client.BindCache<Songbook>();
    }
    private void Client_Closed(object sender, Exception? exception)
    {
        if (exception == null)
            Console.WriteLine("Connection closed without error.");
        else
            Console.WriteLine($"Connection closed due to an error: {exception}");
        
        client.UnbindCache<Group>();
        client.UnbindCache<Member>();
        client.UnbindCache<Membership>();
        client.UnbindCache<Play>();
        client.UnbindCache<Playlog>();
        client.UnbindCache<Setlist>();
        client.UnbindCache<SetlistItem>();
        client.UnbindCache<Song>();
        client.UnbindCache<Songbook>();
    }
}