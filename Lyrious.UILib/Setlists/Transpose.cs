using System;
using System.Text.RegularExpressions;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Moves chords from a song's original key to the key it's played in: by the semitones between
	/// the two keys, spelled with sharps or flats as the playing key is.
	/// </summary>
	public static partial class Transpose
	{
		private static readonly string[] Sharps = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
		private static readonly string[] Flats = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
		// Sharps, but Bb and Eb: for the keys with few sharps (C, G, D and Am, Em, Bm), where those are usual.
		private static readonly string[] FewSharps = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "G#", "A", "Bb", "B"];

		/// <summary>
		/// The semitones from the original key to the playing key, the shorter way round (−5 to +6):
		/// original A and playing G is −2. Zero when either isn't set or isn't a key.
		/// </summary>
		public static int Semitones(string? originalKey, string? playingKey)
		{
			if (Note(originalKey) is not { } from || Note(playingKey) is not { } to)
			{
				return 0;
			}

			var steps = ((to - from) % 12 + 12) % 12;
			return steps > 6 ? steps - 12 : steps;
		}

		/// <summary>Whether a key is written with flats (F, Bb, Eb, …, and Dm, Gm, Cm, …), so its chords are too.</summary>
		public static bool UsesFlats(string? key)
		{
			var name = key?.Trim() ?? "";
			if (name.Length == 0)
			{
				return false;
			}

			if (name.Length > 1 && name[1] == 'b')
			{
				return true;
			}

			var minor = name.EndsWith('m');
			var root = minor ? name[..^1] : name;
			return minor ? root is "D" or "G" or "C" or "F" : root is "F";
		}

		/// <summary>
		/// The text with every chord in brackets moved by the semitones ("[A]" by −2 is "[G]"), slash
		/// bass notes too ("[D/F#]" by +1 is "[Eb/G]" in a flat key). Tags that aren't chords (time
		/// tags, MIDI, "[Riff]") stay as they are.
		/// </summary>
		public static string Chords(string text, int semitones, string? playingKey)
		{
			if (semitones % 12 == 0 || string.IsNullOrEmpty(text))
			{
				return text;
			}

			return BracketTag().Replace(text, match =>
			{
				var tag = match.Groups[1].Value;
				return ChordPro.IsChord(tag) && Chord(tag.Trim(), semitones, playingKey) is { } moved ? $"[{moved}]" : match.Value;
			});
		}

		/// <summary>One chord moved by the semitones ("Am7" by +2 is "Bm7"); null if it isn't a chord.</summary>
		public static string? Chord(string chord, int semitones, string? playingKey)
		{
			var match = ChordShape().Match(chord);
			if (!match.Success)
			{
				return null;
			}

			var names = NotesFor(playingKey);
			var root = Move(match.Groups["root"].Value, semitones, names);
			var bass = match.Groups["bass"].Success ? "/" + Move(match.Groups["bass"].Value, semitones, names) : "";
			return root + match.Groups["kind"].Value + bass;
		}

		/// <summary>
		/// How the playing key spells the notes: with flats in a flat key (F, Bb, …, Dm, Gm, …), with
		/// Bb and Eb but other sharps in a key with few sharps (C, G, D, Am, Em, Bm, or none), else sharps.
		/// </summary>
		private static string[] NotesFor(string? playingKey)
		{
			if (UsesFlats(playingKey))
			{
				return Flats;
			}

			return (playingKey?.Trim() ?? "") is "" or "C" or "G" or "D" or "Am" or "Em" or "Bm" ? FewSharps : Sharps;
		}

		private static string Move(string note, int semitones, string[] names)
		{
			var index = ((Note(note)!.Value + semitones) % 12 + 12) % 12;
			return names[index];
		}

		/// <summary>A note's or key's place from C (0) to B (11), "m" ignored; null if it isn't one.</summary>
		private static int? Note(string? name)
		{
			var text = name?.Trim() ?? "";
			if (text.Length == 0 || char.ToUpperInvariant(text[0]) is not (>= 'A' and <= 'G') and not 'H')
			{
				return null;
			}

			var index = char.ToUpperInvariant(text[0]) switch
			{
				'C' => 0,
				'D' => 2,
				'E' => 4,
				'F' => 5,
				'G' => 7,
				'A' => 9,
				_ => 11, // B, and H as it's called in German and Nordic music.
			};
			if (text.Length > 1)
			{
				index += text[1] switch { '#' => 1, 'b' => -1, _ => 0 };
			}

			return (index + 12) % 12;
		}

		/// <summary>
		/// A chord: a root, what kind of chord (m, 7, maj7, sus4, dim, add9, …) and maybe a slash bass.
		/// Words in brackets ("Bridge") don't fit, so they aren't moved.
		/// </summary>
		[GeneratedRegex(@"^(?<root>[A-H][#b]?)(?<kind>(?:maj|min|dim|aug|sus|add|alt|omit|no|m|M|[0-9]|[#b+\-°ø^()Δ,.])*)(?:/(?<bass>[A-H][#b]?))?$")]
		private static partial Regex ChordShape();

		[GeneratedRegex(@"\[([^\]\n]*)\]")]
		private static partial Regex BracketTag();
	}
}
