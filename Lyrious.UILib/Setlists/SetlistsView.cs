using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The group's setlists, to pick the one to work with or add a new one.</summary>
	public sealed class SetlistsView : UserControl
	{
		/// <summary>Raised when the user picks a setlist.</summary>
		public event Action<SetlistSummary>? SetlistSelected;

		/// <summary>Raised with the name when the user adds a setlist.</summary>
		public event Action<string>? SetlistCreateRequested;

		public SetlistsView(GroupSummary group, IReadOnlyList<SetlistSummary> setlists)
		{
			var rows = setlists
				.Select(setlist => (Control)PageLayout.ListRow(setlist.Name, null, () => SetlistSelected?.Invoke(setlist)))
				.ToArray();

			// "+" reveals a name box above the list; Add (or Enter) creates the setlist.
			var (addButton, nameRow) = PageLayout.AddByName("Add setlist", "Name of the new setlist", name => SetlistCreateRequested?.Invoke(name));

			Content = PageLayout.Page(
				"Setlists",
				new DockPanel { Children = { nameRow, PageLayout.List(rows, "There are no setlists yet.") } },
				subtitle: $"{group.Name} · choose a setlist",
				titleAction: addButton);
		}
	}
}
