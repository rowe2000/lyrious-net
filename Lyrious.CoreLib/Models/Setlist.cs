using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Setlist : Entity, IName
{
	[MaxLength(100)]
	[DataMember] public string Name { get; set; } = "";
	[DataMember] public Guid GroupId { get; set; }

	[JsonIgnore] public Group? Group { get; set; }

	[JsonIgnore] public ObservableList<SetlistItem> SetlistItems { get; set; } = [];

	public override string ToString()
	{
		return Name;
	}

	public SetlistItem AddSong(Song song)
	{
		var setlistItem = SetlistItem.Create(song, this, SetlistItems.Count);
		SetlistItems.Add(setlistItem);
		song.SetlistItems.Add(setlistItem);

		return setlistItem;
	}

	public IEnumerable<SetlistItem> AddSongs(IEnumerable<Song> songs)
	{
		return songs.Select(AddSong);
	}

	public static Setlist Create(Group group, string name)
	{
		return new Setlist
		{
			Name = name,
			Group = group,
			GroupId = group.Id,
		};
	}
}