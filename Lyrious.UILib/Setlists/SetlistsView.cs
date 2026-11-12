using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The group's setlists, to pick the one to work with.</summary>
	public sealed class SetlistsView : UserControl
	{
		/// <summary>Raised when the user picks a setlist.</summary>
		public event Action<SetlistSummary>? SetlistSelected;

		public SetlistsView(GroupSummary group, IReadOnlyList<SetlistSummary> setlists)
		{
			var rows = setlists
				.Select(setlist => (Control)PageLayout.ListRow(setlist.Name, null, () => SetlistSelected?.Invoke(setlist)))
				.ToArray();

			Content = PageLayout.Page(
				"Setlists",
				PageLayout.List(rows, "There are no setlists yet."),
				subtitle: $"{group.Name} · choose a setlist");
		}
	}
}
