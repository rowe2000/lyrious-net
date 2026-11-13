using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The member's bands (groups) with their members, to pick the one to work with or start a new one.</summary>
	public sealed class GroupsView : UserControl
	{
		/// <summary>Raised when the user picks a band.</summary>
		public event Action<GroupSummary>? GroupSelected;

		/// <summary>Raised with the name when the user adds a band.</summary>
		public event Action<string>? GroupCreateRequested;

		/// <param name="members">Each band's members, keyed by group id.</param>
		public GroupsView(IReadOnlyList<GroupSummary> groups, IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members, Guid? currentGroupId)
		{
			var rows = groups
				.Select(group => (Control)GroupRow(group, members.GetValueOrDefault(group.Id) ?? [], group.Id == currentGroupId))
				.ToArray();

			// "+" reveals a name box above the list; Add (or Enter) creates the band.
			var (addButton, nameRow) = PageLayout.AddByName("Add band", "Name of the new band", name => GroupCreateRequested?.Invoke(name));

			Content = PageLayout.Page(
				"Bands",
				new DockPanel { Children = { nameRow, PageLayout.List(rows, "You're not a member of a band yet. Add one with +, or ask a band admin to invite you.") } },
				subtitle: "Choose a band",
				titleAction: addButton);
		}

		/// <summary>One band: its name (and "current"), then its members, one per line.</summary>
		private Button GroupRow(GroupSummary group, IReadOnlyList<GroupMember> members, bool isCurrent)
		{
			var header = new DockPanel();
			if (isCurrent)
			{
				var current = new TextBlock { Text = "current", Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
				DockPanel.SetDock(current, Dock.Right);
				header.Children.Add(current);
			}

			header.Children.Add(new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

			var content = new StackPanel { Spacing = 2, Children = { header } };
			content.Children.AddRange(members.Select(member => (Control)new TextBlock
			{
				Text = MemberText(member),
				FontSize = 13,
				Opacity = member.Approved ? 0.8 : 0.55,
				TextTrimming = TextTrimming.CharacterEllipsis,
			}));

			var button = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 12),
			};
			button.Click += (_, _) => GroupSelected?.Invoke(group);
			return button;
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
