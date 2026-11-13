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
		/// <summary>The address of the Lyrious API to sync with, e.g. http://localhost:7000.</summary>
		public const string Host = "host";
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
