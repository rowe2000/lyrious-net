using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class SetlistItem : EntityBase
{
    [Member] public int Position { get; set; } = -1;

    [Member] public Guid SongId { get; set; }
    [Member] public Guid SetlistId { get; set; }

    public Song Song { get; set; }
    public Setlist Setlist { get; set; }


    public static SetlistItem Create(Song song, Setlist setlist, int position = -1)
    {
        return new SetlistItem
        {
            Song = song,
            Setlist = setlist,
            Position = position
        };
    }
}