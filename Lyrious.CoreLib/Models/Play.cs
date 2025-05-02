using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Lyrious.CoreLib.Models;

public sealed class Play : Entity
{
	[MaxLength(10)]
    [DataMember] public string KeyString { get; set; } = "";
    [DataMember] public float Tempo { get; set; }
    [DataMember] public int DurationMilliSeconds { get; set; }

    [DataMember] public Guid ConductorId { get; set; }
    [DataMember] public Guid PlaylogId { get; set; }
    [DataMember] public Guid SongId { get; set; }

    [JsonIgnore] public Member? Conductor { get; set; }
    [JsonIgnore] public Playlog? Playlog { get; set; }
	[JsonIgnore] public Song? Song { get; set; }

    [NotMapped]
	[JsonIgnore]
    public Key Key
    {
	    get => new(KeyString);
	    set => KeyString = value.ToString();
    }

    [NotMapped]
    [JsonIgnore]
    public TimeSpan Duration
    {
	    get => TimeSpan.FromMilliseconds(DurationMilliSeconds);
	    set => DurationMilliSeconds = (int)value.TotalMilliseconds;
    }

    public override string ToString()
    {
	    return $"{Song?.Name} by {Conductor?.Name} in {Key} / {Tempo} bpm, at {CreatedAt}";
    }

    public static Play Create(Member conductor, Playlog playlog, Song song, string? key = null, float? tempo = null, TimeSpan? duration = null)
    {
        var play = new Play
        {
            Conductor = conductor,
            ConductorId = conductor.Id,
            Playlog = playlog,
            PlaylogId = playlog.Id,
            Song = song,
            SongId = song.Id,
            KeyString = key ?? song.Key,
            Tempo = tempo ?? song.Tempo
        };

		if (duration != null)
        {
            play.Duration = duration.Value;
        }

        return play;
    }
}