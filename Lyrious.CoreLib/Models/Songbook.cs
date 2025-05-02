using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Songbook : Entity, IName
{
	[MaxLength(100)]
    [DataMember] public string Name { get; set; } = "";

    [DataMember] public Guid? GroupId { get; set; }

	[JsonIgnore] public Group? Group { get; set; }

	[JsonIgnore] public ObservableList<Song> Songs { get; } = [];

	public override string ToString()
	{
		return $"{Name}, with {Songs.Count} songs";
	}

	public Song CreateSong(string songName, string key, float tempo = 120, TimeSpan? length = null)
    {
	    var song = Song.Create(this, songName, key, tempo, length);
	    Songs.Add(song);
	    return song;
    }

    public static Songbook Create(Group group, string name)
    {
	    return new Songbook
	    {
		    Name = name,
		    Group = group,
		    GroupId = group.Id,
	    };
    }
}