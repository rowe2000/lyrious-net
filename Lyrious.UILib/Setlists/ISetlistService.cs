using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// What the setlist and lyrics pages need from the outside world. Each host supplies an
	/// implementation (e.g. one that calls the Lyrious API) through <see cref="App.SetlistService"/>.
	/// Methods throw when the server can't be reached; the pages report that.
	/// </summary>
	public interface ISetlistService
	{
		/// <summary>The logged-in member's group (their joined group, else their first group), or null if they have none.</summary>
		Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default);

		Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default);

		/// <summary>The setlist, or null if it doesn't exist (any more).</summary>
		Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default);

		Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default);

		/// <summary>The setlist's songs in order.</summary>
		Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>One setlist entry with its song, or null if it doesn't exist (any more).</summary>
		Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default);

		/// <summary>Makes the setlist the group's current one (and clears the current song).</summary>
		Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default);

		/// <summary>Makes the entry the group's current song (and its setlist the current setlist).</summary>
		Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default);
	}

	public sealed record GroupSummary(Guid Id, string Name, Guid? CurrentSetlistId, Guid? CurrentSetlistItemId);

	public sealed record SetlistSummary(Guid Id, string Name, Guid GroupId);

	public sealed record SongDetails(Guid Id, string Name, string Key, float Tempo, string Beat, string Lyrics);

	/// <summary>A song at a position in a setlist.</summary>
	public sealed record SetlistEntry(Guid Id, Guid SetlistId, int Position, SongDetails Song);

	/// <summary>Used until a host provides a real service, so the pages say why nothing loads.</summary>
	public sealed class UnconfiguredSetlistService : ISetlistService
	{
		private static Exception NotConnected() => new InvalidOperationException("This app isn't connected to a Lyrious server yet.");

		public Task<GroupSummary?> GetCurrentGroupAsync(CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SetlistSummary>> GetSetlistsAsync(Guid groupId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistSummary?> GetSetlistAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistSummary> CreateSetlistAsync(Guid groupId, string name, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<IReadOnlyList<SetlistEntry>> GetEntriesAsync(Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task<SetlistEntry?> GetEntryAsync(Guid setlistItemId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectSetlistAsync(Guid groupId, Guid setlistId, CancellationToken cancellationToken = default) => throw NotConnected();
		public Task SelectSongAsync(Guid groupId, SetlistEntry entry, CancellationToken cancellationToken = default) => throw NotConnected();
	}
}
