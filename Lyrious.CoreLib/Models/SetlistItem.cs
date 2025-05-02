using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class SetlistItem : Entity
{
    [DataMember] public int Position { get; set; } = -1;

    [DataMember] public Guid SongId { get; set; }
    [DataMember] public Guid SetlistId { get; set; }

    [JsonIgnore] public Song? Song { get; set; }
    [JsonIgnore] public Setlist? Setlist { get; set; }

    public override string ToString()
    {
	    return $"{Song?.Name} at {Position} in {Setlist?.Name}";
    }

    public static SetlistItem Create(Song song, Setlist setlist, int position = -1)
    {
        return new SetlistItem
        {
            SongId = song.Id,
	        Song = song,
            SetlistId = setlist.Id,
			Setlist = setlist,
            Position = position
        };
    }
}
