using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Playlog : EntityBase
{
    [Member] public Guid GroupId { get; set; }
    
    public Group Group { get; set; }
 
    public ObservableList<Play> Plays { get; set; } = [];

    public Playlog Create(Group group)
    {
        return new Playlog() { Group = group };
    }
}