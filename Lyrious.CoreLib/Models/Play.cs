using System.ComponentModel.DataAnnotations.Schema;
using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Play : EntityBase
{
    [Member] public string KeyString { get; set; } = "";
    [Member] public float Tempo { get; set; } = 0;
    [Member] public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    [Member] public Guid ConductorId { get; set; }
    [Member] public Guid PlaylogId { get; set; }
    [Member] public Guid SongId { get; set; }

    public Member Conductor { get; set; }
    public Playlog Playlog { get; set; }
    public Song Song { get; set; }

    [NotMapped] private Key? key;

    [NotMapped]
    public Key Key
    {
        get => key ?? new Key(KeyString);
        set => throw new NotImplementedException();
    }


    public static Play Create(Member conductor, Playlog playlog, Song song, string? key = null, float? tempo = null,
        TimeSpan? duration = null)
    {
        var play = new Play
        {
            Conductor = conductor,
            Playlog = playlog,
            Song = song,
            KeyString = key ?? song.Key,
            Tempo = tempo ?? song.Tempo
        };

        if (duration is not null)
        {
            play.Duration = duration.Value;
        }

        return play;
    }
}