using System;
using System.IO;
using System.Threading.Tasks;

namespace Lyrious.UILib
{
	/// <summary>
	/// Sound files kept on this device for songs' media links (a recording to play along with,
	/// instead of a link to one): a file in the app's folder on desktop, the browser's own storage
	/// (IndexedDB) in the web app. They stay on the device they were added on; the song's media link
	/// only names them (see <see cref="Setlists.MediaLinks.SoundLink"/>). Hosts supply one through
	/// <see cref="App.SoundStore"/>; without one, sound files aren't offered.
	/// </summary>
	public interface ISoundStore
	{
		/// <summary>Keeps the sound under the id. Fails with a message for the user (e.g. no room).</summary>
		Task SaveAsync(string id, Stream content);

		/// <summary>Plays the sound (in the app, or in the device's player); false when it isn't on this device.</summary>
		Task<bool> PlayAsync(string id);

		/// <summary>Stops the sound playing in the app, if any.</summary>
		void Stop();

		/// <summary>The sound playing in the app now; null when none is (or the device's player plays it).</summary>
		string? Playing { get; }

		/// <summary>Raised when <see cref="Playing"/> changes, on any thread.</summary>
		event Action? PlayingChanged;
	}
}
