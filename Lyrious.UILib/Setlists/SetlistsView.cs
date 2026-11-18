using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The group's setlists, to pick the one to work with or add a new one; the current one is
	/// highlighted and has a check in its row. A click selects a setlist and shows its toolbar: a
	/// check to make it the current one (unless it is already) and delete. A double click opens it,
	/// to look at its songs, without making it the current one.
	/// </summary>
	public sealed class SetlistsView : UserControl
	{
		/// <summary>Raised when the user opens a setlist (double click), to look at it.</summary>
		public event Action<SetlistSummary>? SetlistSelected;

		/// <summary>Raised when the user makes a setlist the current one (its check).</summary>
		public event Action<SetlistSummary>? SetlistMadeCurrent;

		/// <summary>Raised with the name when the user adds a setlist.</summary>
		public event Action<string>? SetlistCreateRequested;

		/// <summary>Raised when the user confirms deleting a setlist.</summary>
		public event Action<SetlistSummary>? SetlistDeleteRequested;

		/// <summary>The band whose setlists these are.</summary>
		public GroupSummary Group { get; }

		/// <summary>The setlist shown as the current one (when the page was made).</summary>
		public Guid? CurrentSetlistId { get; }

		/// <param name="currentSetlistId">The setlist that is selected now, shown highlighted.</param>
		public SetlistsView(GroupSummary group, IReadOnlyList<SetlistSummary> setlists, Guid? currentSetlistId)
		{
			Group = group;
			CurrentSetlistId = currentSetlistId;
			var list = new SelectableList<SetlistSummary>(
				setlists,
				setlist => PageLayout.RowContent(setlist.Name, setlist.Id == currentSetlistId),
				setlist => setlist.Id == currentSetlistId,
				// A setlist that isn't the current one gets a check to make it current, before the trash.
				setlist => setlist.Id == currentSetlistId
					? [DeleteButton(setlist)]
					: [PageLayout.OpenButton($"Make {setlist.Name} the current setlist", () => SetlistMadeCurrent?.Invoke(setlist)), DeleteButton(setlist)],
				setlist => SetlistSelected?.Invoke(setlist),
				"There are no setlists yet.");

			Control DeleteButton(SetlistSummary setlist) =>
				PageLayout.DeleteButton($"Delete {setlist.Name}", $"Delete the setlist \"{setlist.Name}\"? Its songs stay in the songbook.", () => SetlistDeleteRequested?.Invoke(setlist));

			// "+" reveals a name box above the list; Add (or Enter) creates the setlist.
			var (addButton, nameRow) = PageLayout.AddByName("Add setlist", "Name of the new setlist", name => SetlistCreateRequested?.Invoke(name));

			Content = PageLayout.Page(
				"Setlists",
				new DockPanel { Children = { nameRow, list } },
				subtitle: $"{group.Name} · choose a setlist",
				titleAction: addButton,
				closable: true);
		}
	}
}
