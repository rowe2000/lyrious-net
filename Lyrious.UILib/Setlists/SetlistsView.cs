using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The group's setlists, to pick the one to work with or add a new one; the current one is
	/// highlighted. A click selects a setlist and shows its toolbar (open, delete); a double click opens it.
	/// </summary>
	public sealed class SetlistsView : UserControl
	{
		/// <summary>Raised when the user opens a setlist (double click, or the open button).</summary>
		public event Action<SetlistSummary>? SetlistSelected;

		/// <summary>Raised with the name when the user adds a setlist.</summary>
		public event Action<string>? SetlistCreateRequested;

		/// <summary>Raised when the user confirms deleting a setlist.</summary>
		public event Action<SetlistSummary>? SetlistDeleteRequested;

		/// <param name="currentSetlistId">The setlist that is selected now, shown highlighted.</param>
		public SetlistsView(GroupSummary group, IReadOnlyList<SetlistSummary> setlists, Guid? currentSetlistId)
		{
			var list = new SelectableList<SetlistSummary>(
				setlists,
				setlist => PageLayout.RowContent(setlist.Name, null),
				setlist => setlist.Id == currentSetlistId,
				setlist =>
				[
					PageLayout.OpenButton($"Open {setlist.Name}", () => SetlistSelected?.Invoke(setlist)),
					PageLayout.DeleteButton($"Delete {setlist.Name}", $"Delete the setlist \"{setlist.Name}\"? Its songs stay in the songbook.", () => SetlistDeleteRequested?.Invoke(setlist)),
				],
				setlist => SetlistSelected?.Invoke(setlist),
				"There are no setlists yet.");

			// "+" reveals a name box above the list; Add (or Enter) creates the setlist.
			var (addButton, nameRow) = PageLayout.AddByName("Add setlist", "Name of the new setlist", name => SetlistCreateRequested?.Invoke(name));

			Content = PageLayout.Page(
				"Setlists",
				new DockPanel { Children = { nameRow, list } },
				subtitle: $"{group.Name} · choose a setlist",
				titleAction: addButton);
		}
	}
}
