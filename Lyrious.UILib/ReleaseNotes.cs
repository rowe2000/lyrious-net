using System;
using System.Collections.Generic;
using System.Linq;

namespace Lyrious.UILib
{
	/// <summary>What a release brought: when, its version, and its notes (one line each).</summary>
	public sealed record ReleaseNote(DateTime Date, string Version, IReadOnlyList<string> Notes);

	/// <summary>
	/// The app's release notes, newest first: shown on the About page, and after login ("What's new
	/// in Lyrious") for the releases since the member's last visit. Add a new entry at the top for
	/// each release.
	/// </summary>
	public static class ReleaseNotes
	{
		public static IReadOnlyList<ReleaseNote> All { get; } =
		[
			new(new DateTime(2026, 11, 18), "0.9",
			[
				"Your private band: every member has one, to keep songs and setlists of their own without joining a band.",
				"Band and Setlist pages show your bands and setlists as buttons to pick from; click a name to rename it.",
				"The songbook lets you switch between your private songbook and your band's.",
				"A '|' in the lyrics marks a new bar, like [+1], so the lyrics scroll in time.",
				"Chord grid: chords in bar columns over the words; turn it on with the grid button on the lyrics page.",
				"Spotify and YouTube songs play inside the app, in a small player under the media button, and stop at the song's end.",
				"Delete buttons turn red, and the lyrics use the whole width of the window.",
				"After login you see what's new since your last visit; Exit and About are at the bottom of the menu.",
			]),
		];

		/// <summary>The releases after <paramref name="since"/> (all of them when null), newest first.</summary>
		public static IReadOnlyList<ReleaseNote> Since(DateTime? since)
			=> since is { } time ? All.Where(note => note.Date > time.Date).ToList() : All;

		/// <summary>The app's version, as shown on the About page (e.g. "1.0.0"), from the app's build.</summary>
		public static string AppVersion
		{
			get
			{
				var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(ReleaseNotes).Assembly;
				var informational = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
					.OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
				// Without the build's source revision ("1.0.0+abc123").
				return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "";
			}
		}
	}
}
