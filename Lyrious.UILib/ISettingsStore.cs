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

		/// <summary>The address of the Lyrious API to sync with, e.g. http://localhost:5180.</summary>
		public const string Host = "host";

		/// <summary>"false" turns off going on to the next song when stopping at the end of the lyrics (on by default).</summary>
		public const string NextSongOnStopAtEnd = "next-song-on-stop-at-end";

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

		/// <summary>The size of the whole interface, e.g. "1.25"; none for normal size (see <see cref="UiScale"/>).</summary>
		public const string UiScale = "ui-scale";

		/// <summary>"true" shows lyrics with chords as a chord grid (chords only, by bar) in the scroll view; off unless set.</summary>
		public const string ChordGrid = "chord-grid";

		/// <summary>The lyrics' zoom in the scroll view (pinch), e.g. "1.4"; none for normal size.</summary>
		public const string LyricsZoom = "lyrics-zoom";

		/// <summary>"true" sizes the whole app to the window (see <see cref="Setlists.DisplaySettings.DynamicZoom"/>); off unless set.</summary>
		public const string DynamicZoom = "dynamic-zoom";

		/// <summary>"true" shares the app on the local network (see <see cref="ILanSharing"/>); off unless set.</summary>
		public const string LanSharing = "lan-sharing";

		/// <summary>The port the app is shared on, e.g. "8080"; none for <see cref="LanSharingSettings.DefaultPort"/>.</summary>
		public const string LanSharingPort = "lan-sharing-port";

		/// <summary>"true" shows the song's name and details above the lyrics in the scroll view; off unless set.</summary>
		public const string ShowSongHeader = "show-song-header";
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
