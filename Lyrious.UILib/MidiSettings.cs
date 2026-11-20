using System.Collections.Generic;

namespace Lyrious.UILib
{
	/// <summary>
	/// The member's MIDI settings, kept with the user (the same on all their devices, see
	/// <see cref="UserSettings"/>): their MIDI expressions (shortcuts in songs' MIDI tags) and the MIDI
	/// commands sent for every song. Until they save their own, the band's (as bands kept them before)
	/// count, so MIDI set up then goes on working.
	/// </summary>
	public static class MidiSettings
	{
		/// <summary>The member's MIDI expressions; the band's (<paramref name="bandJson"/>) while they have none of their own.</summary>
		public static IReadOnlyList<MidiExpression> Expressions(string? bandJson)
			=> Midi.ExpressionsFromJson(App.Settings.Get(SettingKeys.MidiExpressions) is { } own ? own : bandJson);

		/// <summary>The member's MIDI commands for every song; the band's (<paramref name="bandJson"/>) while they have none of their own.</summary>
		public static MidiSongCommands Commands(string? bandJson)
			=> Midi.SongCommandsFromJson(App.Settings.Get(SettingKeys.MidiCommands) is { } own ? own : bandJson);

		/// <summary>Saves the member's MIDI expressions (kept with the user).</summary>
		public static void SaveExpressions(IReadOnlyList<MidiExpression> expressions)
		{
			// "[]" rather than nothing, so none of one's own isn't taken for the band's.
			App.Settings.Set(SettingKeys.MidiExpressions, Midi.ExpressionsToJson(expressions));
			UserSettings.ChangedHere();
		}

		/// <summary>Saves the member's MIDI commands for every song (kept with the user).</summary>
		public static void SaveCommands(MidiSongCommands commands)
		{
			App.Settings.Set(SettingKeys.MidiCommands, Midi.SongCommandsToJson(commands) ?? "{}");
			UserSettings.ChangedHere();
		}
	}
}
