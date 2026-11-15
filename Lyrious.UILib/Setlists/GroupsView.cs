using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The member's bands (groups) with their members, to pick the one to work with or start a new
	/// one. A click selects a band and shows its toolbar (✓ joins it; the joined band has none); a
	/// double click joins it too.
	/// </summary>
	public sealed class GroupsView : UserControl
	{
		private const string CheckIcon = "M5,12 L10,17 L19,7";

		/// <summary>Raised when the user joins a band (double click, or the ✓ button).</summary>
		public event Action<GroupSummary>? GroupSelected;

		/// <summary>Raised with the name when the user adds a band.</summary>
		public event Action<string>? GroupCreateRequested;

		/// <param name="members">Each band's members, keyed by group id.</param>
		public GroupsView(IReadOnlyList<GroupSummary> groups, IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members, Guid? currentGroupId)
		{
			var list = new SelectableList<GroupSummary>(
				groups,
				group => GroupContent(group, members.GetValueOrDefault(group.Id) ?? [], joined: group.Id == currentGroupId),
				group => group.Id == currentGroupId,
				group => group.Id == currentGroupId ? [] : [JoinButton(group)],
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

		/// <summary>✓: join the band (make it the current one).</summary>
		private Button JoinButton(GroupSummary group)
		{
			var button = HomeView.IconButton(CheckIcon, $"Join {group.Name}");
			button.Click += (_, _) => GroupSelected?.Invoke(group);
			return button;
		}

		/// <summary>One band: its name, then its members, one per line; the joined band has a ✓ at the right.</summary>
		private static Control GroupContent(GroupSummary group, IReadOnlyList<GroupMember> members, bool joined)
		{
			var details = BandDetails(group, members);
			if (!joined)
			{
				return details;
			}

			// Drawn in the row's text colour, so it shows on the highlight.
			var check = new Path
			{
				Data = Geometry.Parse(CheckIcon),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 24,
				Height = 24,
				Margin = new Thickness(12, 0, 0, 0),
				VerticalAlignment = VerticalAlignment.Center,
			};
			check.Bind(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) } });
			ToolTip.SetTip(check, "Joined");
			DockPanel.SetDock(check, Dock.Right);
			return new DockPanel { Children = { check, details } };
		}

		private static Control BandDetails(GroupSummary group, IReadOnlyList<GroupMember> members)
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
