using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The member's bands (groups) with their members, to pick the one to work with or start a new
	/// one. A click selects a band and shows its toolbar; a double click makes it the current band.
	/// </summary>
	public sealed class GroupsView : UserControl
	{
		/// <summary>Raised when the user opens a band (double click, or the open button).</summary>
		public event Action<GroupSummary>? GroupSelected;

		/// <summary>Raised with the name when the user adds a band.</summary>
		public event Action<string>? GroupCreateRequested;

		/// <param name="members">Each band's members, keyed by group id.</param>
		public GroupsView(IReadOnlyList<GroupSummary> groups, IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members, Guid? currentGroupId)
		{
			var list = new SelectableList<GroupSummary>(
				groups,
				group => GroupContent(group, members.GetValueOrDefault(group.Id) ?? []),
				group => group.Id == currentGroupId,
				group => [PageLayout.OpenButton($"Open {group.Name}", () => GroupSelected?.Invoke(group))],
				group => GroupSelected?.Invoke(group),
				"You're not a member of a band yet. Add one with +, or ask a band admin to invite you.");

			// "+" reveals a name box above the list; Add (or Enter) creates the band.
			var (addButton, nameRow) = PageLayout.AddByName("Add band", "Name of the new band", name => GroupCreateRequested?.Invoke(name));

			Content = PageLayout.Page(
				"Bands",
				new DockPanel { Children = { nameRow, list } },
				subtitle: "Choose a band",
				titleAction: addButton,
				closable: true);
		}

		/// <summary>One band: its name, then its members, one per line.</summary>
		private static Control GroupContent(GroupSummary group, IReadOnlyList<GroupMember> members)
		{
			var content = new StackPanel { Spacing = 2 };
			content.Children.Add(new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
			content.Children.AddRange(members.Select(member => (Control)new TextBlock
			{
				Text = MemberText(member),
				FontSize = 13,
				Opacity = member.Approved ? 0.8 : 0.55,
				TextTrimming = TextTrimming.CharacterEllipsis,
			}));
			return content;
		}

		/// <summary>E.g. "Maria Berggren · conductor" or "Chrille · invited"; plain members show just the name.</summary>
		private static string MemberText(GroupMember member)
		{
			var notes = new List<string>();
			if (member.Role != GroupRole.Member)
			{
				notes.Add(member.Role.ToString().ToLowerInvariant());
			}

			if (!member.Approved)
			{
				notes.Add("invited");
			}

			return notes.Count == 0 ? member.Name : $"{member.Name} · {string.Join(", ", notes)}";
		}
	}
}
