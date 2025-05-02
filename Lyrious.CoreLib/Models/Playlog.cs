using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Playlog : Entity
{
    [DataMember] public Guid GroupId { get; set; }

    [JsonIgnore] public Group? Group { get; set; }

    [JsonIgnore] public ObservableList<Play> Plays { get; } = [];

    public override string ToString()
    {
	    return $"Played {Plays.Count} songs at {CreatedAt}";
    }

    public static Playlog Create(Group group)
    {
        return new Playlog
        {
	        Group = group,
            GroupId = group.Id,
		};
    }

    public Play CreatePlay(Song song, Member conductor, string? key = null, float? tempo = null)
    {
		var play = Play.Create(conductor, this, song, key ?? song.Key, tempo ?? song.Tempo);
		Plays.Add(play);
		return play;
    }
}