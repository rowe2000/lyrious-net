using System;

namespace Lyrious.UILib
{
	/// <summary>
	/// The MIDI module: everything the app does with MIDI (this folder: sending a song's MIDI tags and the
	/// MIDI commands for every song, the transpose to Yamaha keyboards, and their settings). Off unless
	/// the user turns it on in Settings (kept with the user, see <see cref="SettingKeys.Midi"/>): then
	/// MIDI is sent and its settings show. Off, nothing is sent and nothing about MIDI shows.
	/// </summary>
	public static class MidiModule
	{
		/// <summary>Whether the user has turned MIDI on. Kept with the user; off unless turned on.</summary>
		public static bool Enabled
		{
			get => App.Settings.Get(SettingKeys.Midi) == "true";
			set
			{
				App.Settings.Set(SettingKeys.Midi, value ? "true" : null);
				// Kept with the user: sent to the server too.
				UserSettings.ChangedHere();
				EnabledChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="Enabled"/> is changed here (a change from another device raises <see cref="UserSettings.Changed"/>).</summary>
		public static event Action? EnabledChanged;

		/// <summary>Where MIDI is sent: this device's MIDI outputs (see <see cref="App.Midi"/>) when MIDI is on; else none.</summary>
		public static IMidiOutput? Output => Enabled ? App.Midi : null;

		/// <summary>Whether MIDI can be sent from this device at all (whether it's on or not).</summary>
		public static bool Available => App.Midi != null;
	}
}
