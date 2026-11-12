using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.UILib.Setlists
{
	public enum StartPageKind
	{
		/// <summary>The member isn't in any group yet.</summary>
		NoGroup,
		/// <summary>The group's last selected song: show its lyrics.</summary>
		Lyrics,
		/// <summary>No current song, but a current setlist: show its songs.</summary>
		Setlist,
		/// <summary>No current setlist: show the group's setlists to pick one.</summary>
		Setlists,
	}

	/// <summary>Which page the app opens on after login, with the data that page needs.</summary>
	public sealed record StartPage(
		StartPageKind Kind,
		GroupSummary? Group = null,
		SetlistSummary? Setlist = null,
		SetlistEntry? Entry = null,
		IReadOnlyList<SetlistSummary>? Setlists = null)
	{
		public const string DefaultSetlistName = "Setlist 1";

		/// <summary>
		/// Decides where to start: the last selected song's lyrics; else the current setlist; else the
		/// list of setlists, creating "Setlist 1" first if the group has none. A current song or setlist
		/// that no longer exists counts as not selected.
		/// </summary>
		public static async Task<StartPage> DetermineAsync(ISetlistService service, CancellationToken cancellationToken = default)
		{
			var group = await service.GetCurrentGroupAsync(cancellationToken);
			if (group == null)
			{
				return new StartPage(StartPageKind.NoGroup);
			}

			if (group.CurrentSetlistItemId is { } itemId
			    && await service.GetEntryAsync(itemId, cancellationToken) is { } entry
			    && await service.GetSetlistAsync(entry.SetlistId, cancellationToken) is { } songSetlist)
			{
				return new StartPage(StartPageKind.Lyrics, group, songSetlist, entry);
			}

			if (group.CurrentSetlistId is { } setlistId
			    && await service.GetSetlistAsync(setlistId, cancellationToken) is { } setlist)
			{
				return new StartPage(StartPageKind.Setlist, group, setlist);
			}

			var setlists = await service.GetSetlistsAsync(group.Id, cancellationToken);
			if (setlists.Count == 0)
			{
				setlists = [await service.CreateSetlistAsync(group.Id, DefaultSetlistName, cancellationToken)];
			}

			return new StartPage(StartPageKind.Setlists, group, Setlists: setlists);
		}
	}
}
