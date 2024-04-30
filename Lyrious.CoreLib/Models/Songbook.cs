using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Songbook : EntityBase, IName
{
    [Member] public virtual string Name { get; set; } = "";

    [Member] public Guid? GroupId { get; set; }

    public Group? Group { get; set; }

    public ObservableList<Song> Songs { get; } = [];

    public static Songbook Create(string name, Group group, params Song[] songs)
    {
        var songbook = new Songbook { Name = name, Group = group };
        group.Songbooks.Add(songbook);

        foreach (var song in songs)
        {
            songbook.Songs.Add(song);
            song.Songbook = songbook;
        }

        return songbook;
    }
}