using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Song : EntityBase, IName
{
    [Member] public virtual string Name { get; set; } = "";
    [Member] public string Key { get; set; } = "";
    [Member] public float Tempo { get; set; } = 120;
    [Member] public string Beat { get; set; } = "4/4";
    [Member] public TimeSpan Length { get; set; } = TimeSpan.FromMinutes(3);
    [Member] public string Lyrics { get; set; } = "";

    [Member] public Guid SongbookId { get; set; }

    public Songbook Songbook { get; set; }

    public ObservableList<SetlistItem> SetlistItems { get; set; } = [];


    public static Song Create(Songbook songbook, string name, string key = "", int tempo = 120, TimeSpan? length = null)
    {
        length ??= TimeSpan.FromMinutes(3);
        var song = new Song { Songbook = songbook, Name = name, Key = key, Tempo = tempo, Length = length.Value };

        return song;
    }
}