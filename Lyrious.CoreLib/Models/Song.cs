using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Song : Entity, IName
{
	[MaxLength(100)]
    [DataMember] public string Name { get; set; } = "";
	[MaxLength(10)]
    /// <summary>The playing key: the key the band plays the song in (e.g. "G"); empty if not set.</summary>
    [DataMember] public string Key { get; set; } = "";
    /// <summary>The key the song was written or recorded in; empty if not set.</summary>
    [MaxLength(10)]
    [DataMember] public string OriginalKey { get; set; } = "";
    /// <summary>Who wrote or recorded the song (e.g. "The Beatles"); empty if not set.</summary>
    [MaxLength(100)]
    [DataMember] public string Artist { get; set; } = "";
    [DataMember] public float Tempo { get; set; } = 120;
	[MaxLength(10)]
    [DataMember] public string Beat { get; set; } = "4/4";
    [DataMember] public int LengthMilliSeconds { get; set; } = TimeSpan.FromMinutes(3).Milliseconds;
    [MaxLength(5000)]
    [DataMember] public string Lyrics { get; set; } = "";
    /// <summary>A link to listen to or watch the song, e.g. on Spotify or YouTube; empty if none.</summary>
    [MaxLength(500)]
    [DataMember] public string MediaLink { get; set; } = "";
    /// <summary>Tags to sort the song by (e.g. "christmas, ballad"), separated by commas; empty if none.</summary>
    [MaxLength(MaxTagsLength)]
    [DataMember] public string Tags { get; set; } = "";

    public const int MaxTagsLength = 500;

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