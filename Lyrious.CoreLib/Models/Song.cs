using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Song : Entity, IName
{
	[MaxLength(100)]
    [DataMember] public string Name { get; set; } = "";
	[MaxLength(10)]
    [DataMember] public string Key { get; set; } = "";
    [DataMember] public float Tempo { get; set; } = 120;
	[MaxLength(10)]
    [DataMember] public string Beat { get; set; } = "4/4";
    [DataMember] public int LengthMilliSeconds { get; set; } = TimeSpan.FromMinutes(3).Milliseconds;
    [MaxLength(5000)]
    [DataMember] public string Lyrics { get; set; } = "";

    [DataMember] public Guid SongbookId { get; set; }
    [JsonIgnore] public Songbook? Songbook { get; set; }

	[JsonIgnore] public TimeSpan Length
	{
		get => TimeSpan.FromMilliseconds(LengthMilliSeconds);
		set => LengthMilliSeconds = (int)value.TotalMilliseconds;
	}

	[JsonIgnore] public ObservableList<SetlistItem> SetlistItems { get; set; } = [];

	public override string ToString()
	{
		return $"{Name} ({Key}, {Beat}, {Tempo})";
	}

	public SetlistItem AddToSetlist(Setlist setlist)
	{
		var setlistItem = SetlistItem.Create(this, setlist, SetlistItems.Count);
		SetlistItems.Add(setlistItem);
		setlist.SetlistItems.Add(setlistItem);

		return setlistItem;
	}
    public static Song Create(Songbook songbook, string name, string key = "", float tempo = 120, TimeSpan? length = null)
    {
	    return new Song
        {
	        Songbook = songbook, 
	        SongbookId = songbook.Id,
	        Name = name, 
	        Key = key, 
	        Tempo = tempo, 
	        Length = length ?? TimeSpan.FromMinutes(3)
        };
    }
}