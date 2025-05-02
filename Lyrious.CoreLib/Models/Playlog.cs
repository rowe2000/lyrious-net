using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Playlog : Entity
{
    [DataMember] public Guid GroupId { get; set; }

    [JsonIgnore] public Group? Group { get; set; }

    [JsonIgnore] public ObservableList<Play> Plays { get; } = [];

    public static Playlog Create(Group group)
    {
        return new Playlog
        {
	        Group = group,
            GroupId = group.Id,
		};
    }

    public Play CreatePlay(Member conductor, Playlog playlog, Song song, string? key = null, float? tempo = null)
    {
		var play = Play.Create(conductor, playlog, song, key ?? song.Key, tempo ?? song.Tempo);
		playlog.Plays.Add(play);
		return play;
    }
}