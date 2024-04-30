using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Setlist : EntityBase, IName
{
    [Member] public virtual string Name { get; set; } = "";
    [Member] public Guid GroupId { get; set; }

    public Group Group { get; set; }

    public ObservableList<SetlistItem> SetlistItems { get; set; } = [];

    public static Setlist Create(string name, Group group, params Song[] songs)
    {
        var setlist = new Setlist { Name = name, Group = group };
        foreach (var song in songs)
        {
            var setlistItem = new SetlistItem { Song = song, Setlist = setlist };
            setlist.SetlistItems.Add(setlistItem);
        }

        return setlist;
    }
}