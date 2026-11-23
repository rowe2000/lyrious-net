using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Lyrious.CoreLib.Models;

/// <summary>
/// A member's own settings for a song, seen only by them (on all their devices): a transpose of
/// the chords, their own version of the lyrics, and the sound their keyboard plays it with. One per
/// member and song.
/// </summary>
public sealed class SongSetting : Entity
{
	public const int MinTranspose = -11;
	public const int MaxTranspose = 11;
	public const int MaxLyricsLength = 5000;
	public const int MaxMidiSoundLength = 100;

	[DataMember] public Guid MemberId { get; set; }
	[DataMember] public Guid SongId { get; set; }

	/// <summary>Semitones the chords are moved by, on top of the song's own (−11 to +11; 0 for none).</summary>
	[DataMember] public int Transpose { get; set; }

	/// <summary>The transpose is turned off for now (kept, to turn on again as it was).</summary>
	[DataMember] public bool TransposeOff { get; set; }

	/// <summary>The member's own lyrics, shown instead of the band's; empty to use the band's.</summary>
	[MaxLength(MaxLyricsLength)]
	[DataMember] public string Lyrics { get; set; } = "";

	/// <summary>
	/// The sound the member's keyboard plays the song with, sent when the song is chosen: its MIDI
	/// channel, bank (MSB, LSB) and program, and its name ("1,63,0,5,Rock Organ"); empty for none.
	/// </summary>
	[MaxLength(MaxMidiSoundLength)]
	[DataMember] public string MidiSound { get; set; } = "";
}
