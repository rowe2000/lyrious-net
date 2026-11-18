using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Lyrious.UILib
{
	/// <summary>
	/// Sound files kept on this device for songs' media links (a recording to play along with,
	/// instead of a link to one): a file in the app's folder on desktop, the browser's own storage
	/// (IndexedDB) in the web app. They stay on the device they were added on; the song's media link
	/// only names them (see <see cref="Setlists.MediaLinks.SoundLink"/>), and the server keeps a copy
	/// for the band's other devices (see <see cref="Setlists.SoundSync"/>). Hosts supply one through
	/// <see cref="App.SoundStore"/>; without one, sound files aren't offered.
	/// </summary>
	public interface ISoundStore
	{
		/// <summary>Keeps the sound under the id. Fails with a message for the user (e.g. no room).</summary>
		Task SaveAsync(string id, Stream content);

		/// <summary>The sound's content; null when it isn't on this device.</summary>
		Task<Stream?> OpenAsync(string id);

		/// <summary>The sounds kept on this device, with their sizes.</summary>
		Task<IReadOnlyList<StoredSound>> ListAsync();

		/// <summary>Removes the sound from this device (stopping it first if it plays); nothing when it isn't here.</summary>
		Task DeleteAsync(string id);

		/// <summary>Plays the sound (in the app, or in the device's player); false when it isn't on this device.</summary>
		Task<bool> PlayAsync(string id);

		/// <summary>Stops the sound playing in the app, if any.</summary>
		void Stop();

		/// <summary>The sound playing in the app now; null when none is (or the device's player plays it).</summary>
		string? Playing { get; }

		/// <summary>Raised when <see cref="Playing"/> changes, on any thread.</summary>
		event Action? PlayingChanged;

		/// <summary>Whether <see cref="Speed"/> can be changed here (not when the device's own player plays the sounds).</summary>
		bool CanChangeSpeed { get; }

		/// <summary>Whether <see cref="Pitch"/> can be changed here.</summary>
		bool CanChangePitch { get; }

		/// <summary>
		/// How fast sounds play, without changing their pitch: 1 as recorded, 0.75 three quarters as
		/// fast (to practise along). Applies at once to the sound playing, and to those played after.
		/// </summary>
		double Speed { get; set; }

		/// <summary>
		/// The pitch in semitones, without changing the speed: 0 as recorded, -2 a whole tone lower (to
		/// play along in another key). Applies at once to the sound playing, and to those played after.
		/// </summary>
		double Pitch { get; set; }
	}

	/// <summary>A sound kept on this device: its id (see <see cref="Setlists.SoundFile"/>) and its size in bytes.</summary>
	public sealed record StoredSound(string Id, long Size);
}
