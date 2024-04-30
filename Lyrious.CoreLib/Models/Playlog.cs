using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Playlog : EntityBase
{
	public Playlog()
	{
	}
	private Playlog(Group group)
    {
        Group = group;
    }

    [Member] public Guid GroupId { get; set; }

    public Group Group { get; }

    public ObservableList<Play> Plays { get; } = [];

    public Playlog Create(Group group)
    {
        return new Playlog(group);
    }
}