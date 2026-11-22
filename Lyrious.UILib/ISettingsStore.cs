using System.Collections.Generic;

namespace Lyrious.UILib
{
	/// <summary>
	/// Small settings kept on this device between runs (e.g. the API host). Each host app supplies one
	/// through <see cref="App.Settings"/>: a file on desktop, the browser's IndexedDB on the web.
	/// </summary>
	public interface ISettingsStore
	{
		string? Get(string key);

		/// <summary>Stores the value; null removes it.</summary>
		void Set(string key, string? value);
	}

	/// <summary>Keys of the settings the app uses.</summary>
	public static class SettingKeys
	{
		/// <summary>When the member last saw the screen after login (see <see cref="Setlists.OnboardingView"/>), round-trip format; none before the first time.</summary>
		public static string LastVisit(string userName) => $"last-visit:{userName.ToLowerInvariant()}";

		/// <summary>The address of the Lyrious API to sync with, e.g. https://www.lyrious.se; none for <see cref="DefaultServer"/>.</summary>
		/// <summary>The Lyrious server the apps use until another is chosen on the Sync page.</summary>
		public const string DefaultServer = "https://www.lyrious.se";

		public const string Host = "host";

		/// <summary>"false" stops following and sending the band's song, setlist and play/stop (on by default).</summary>
		public const string BandSync = "band-sync";

		/// <summary>The highlight colour, "#RRGGBB"; none for the system's accent colour.</summary>
		public const string HighlightColor = "highlight-color";

		/// <summary>The chord colour, "#RRGGBB"; none for the theme's.</summary>
		public const string ChordColor = "chord-color";

		/// <summary>The colour of level 1 headings in the lyrics, "#RRGGBB"; none for the text colour.</summary>
		public const string Heading1Color = "heading1-color";

		/// <summary>The colour of level 2 headings in the lyrics, "#RRGGBB"; none for the text colour.</summary>
		public const string Heading2Color = "heading2-color";

		/// <summary>The colour of the other headings in the lyrics (levels 3 to 6), "#RRGGBB"; none for the text colour.</summary>
		public const string HeadingColor = "heading-color";

		/// <summary>
		/// The user name whose own settings (see <see cref="UserSettings"/>) were changed here and not yet
		/// sent to the server; none if nothing is waiting.
		/// </summary>
		public const string UserSettingsPending = "user-settings-pending";

		/// <summary>The size of the whole interface, e.g. "1.25"; none for normal size (see <see cref="UiScale"/>).</summary>
		public const string UiScale = "ui-scale";

		/// <summary>"true" shows lyrics with chords as a chord grid (chords only, by bar) in the scroll view; off unless set.</summary>
		public const string ChordGrid = "chord-grid";

		/// <summary>
		/// "false" hides the chord grid button on the lyrics page (and the lyrics show chords over the
		/// words, not as a grid); on unless turned off. Kept with the user.
		/// </summary>
		public const string ChordGridButton = "chord-grid-button";

		/// <summary>
		/// The kinds of settings the Settings page hides: some of "Device", "User" and "Band", separated by
		/// commas; none (all shown) unless set. This device only.
		/// </summary>
		public const string SettingsFilter = "settings-filter";

		/// <summary>"true" shows the app full screen (desktop and browser; F11 too); off unless set. This device only.</summary>
		public const string FullScreen = "full-screen";

		/// <summary>The language chosen: "sv" or "en"; none for the device's (see <see cref="Localization.Language"/>). Kept with the user.</summary>
		public const string Language = "language";

		/// <summary>The theme: "light", or "system" (the computer's); none for dark (see <see cref="ThemeSettings"/>). Kept with the user.</summary>
		public const string Theme = "theme";

		/// <summary>
		/// "true" counts the member's own transpose as a capo (in frets, −11 to +11, moving the chords
		/// down: the transpose −n); none as a transpose in semitones (−11 to +11). Kept with the user.
		/// </summary>
		public const string CapoMode = "capo-mode";

		/// <summary>
		/// The Yamaha keyboards that get the member's transpose of the song shown, on the MIDI outputs, as
		/// their keyboard transpose (see <see cref="Midi.YamahaTranspose"/>): some of "MODX", "YC" and "CK",
		/// separated by commas ("true" from before is all of them); none unless set. Kept with the user
		/// (the same keyboards on all their devices).
		/// </summary>
		public const string SendYamahaTranspose = "send-yamaha-transpose";

		/// <summary>
		/// The member's MIDI expressions (shortcuts in songs' MIDI tags), a JSON array (see <see cref="Midi"/>);
		/// none for the band's (see <see cref="MidiSettings"/>). Kept with the user.
		/// </summary>
		public const string MidiExpressions = "midi-expressions";

		/// <summary>
		/// The member's MIDI commands for every song (select, start, stop), a JSON object (see <see cref="Midi"/>);
		/// none for the band's (see <see cref="MidiSettings"/>). Kept with the user.
		/// </summary>
		public const string MidiCommands = "midi-commands";

		/// <summary>The lyrics' zoom in the scroll view (pinch), e.g. "1.4"; none for normal size.</summary>
		public const string LyricsZoom = "lyrics-zoom";

		/// <summary>"false" stops sizing the whole app to the window (see <see cref="Setlists.DisplaySettings.DynamicZoom"/>); on unless set so.</summary>
		public const string DynamicZoom = "dynamic-zoom";

		/// <summary>"true" shares the app on the local network (see <see cref="ILanSharing"/>); off unless set.</summary>
		public const string LanSharing = "lan-sharing";

		/// <summary>The port the app is shared on, e.g. "8080"; none for <see cref="LanSharingSettings.DefaultPort"/>.</summary>
		public const string LanSharingPort = "lan-sharing-port";

		/// <summary>
		/// Where the app's window was when it closed, to open there again (desktop): "x,y,width,height" and
		/// ",max" if it was maximized; the position in screen pixels, the size in the app's units. This device only.
		/// </summary>
		public const string WindowPlacement = "window-placement";
	}

	/// <summary>Used until a host provides a store: settings last only while the app runs.</summary>
	public sealed class MemorySettingsStore : ISettingsStore
	{
		private readonly Dictionary<string, string> values = new();

		public string? Get(string key) => values.TryGetValue(key, out var value) ? value : null;

		public void Set(string key, string? value)
		{
			if (value == null)
			{
				values.Remove(key);
			}
			else
			{
				values[key] = value;
			}
		}
	}
}
