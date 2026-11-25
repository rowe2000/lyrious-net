using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lyrious.UILib
{
	/// <summary>
	/// Records from the device's sound input (its microphone, or a sound card's input) in two
	/// channels, for recording the band as it plays (see <see cref="Setlists.PlayRecorder"/>). Hosts
	/// that can record supply one through <see cref="App.AudioRecorder"/>; without one, recording isn't offered.
	/// </summary>
	public interface IAudioRecorder
	{
		/// <summary>The sound inputs that can be recorded from, by name; none when they can't be told apart here.</summary>
		Task<IReadOnlyList<AudioInput>> GetInputsAsync() => Task.FromResult<IReadOnlyList<AudioInput>>([]);

		/// <summary>
		/// Starts recording from the input (its <see cref="AudioInput.Id"/>; null, or one that isn't
		/// connected, for the device's own choice). The sound is handed to <paramref name="samples"/>
		/// as it comes, on any thread: 16-bit samples, the two channels interleaved (left, right; a
		/// one-channel input is given in both), the count of samples in the array that are new.
		/// Returns the sample rate (samples per second, per channel). Fails with a message for the
		/// user when it can't record (no input, or recording isn't allowed).
		/// </summary>
		Task<int> StartAsync(string? inputId, Action<short[], int> samples);

		/// <summary>Stops recording; all the sound recorded has been handed over when it's done.</summary>
		Task StopAsync();
	}

	/// <summary>A sound input to record from: its id on the device, and its name as the device gives it.</summary>
	public sealed record AudioInput(string Id, string Name);
}
