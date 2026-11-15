using System.Collections.Generic;

namespace Lyrious.UILib
{
	/// <summary>
	/// Small settings kept on this device between runs (e.g. the API host). Each host app supplies one
	/// through <see cref="App.Settings"/>: a file on desktop, the browser's local storage on the web.
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

		/// <summary>The size of the whole interface, e.g. "1.25"; none for normal size (see <see cref="UiScale"/>).</summary>
		public const string UiScale = "ui-scale";
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
