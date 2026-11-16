using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
	/// double click joins it too. Above the list, the selected band's details: who's in it (role,
	/// invited or not, last login), Join, and for the band's admins tools to invite, set roles,
	/// remove members and delete the band.
	/// </summary>
	public sealed class GroupsView : UserControl
	{
		private const string CheckIcon = "M5,12 L10,17 L19,7";
		private const string RemoveIcon = "M6,6 L18,18 M18,6 L6,18";

		private readonly IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members;
		private readonly Guid? currentGroupId;
		private readonly ContentControl details = new();
		private readonly SelectableList<GroupSummary> list;

		/// <summary>The band whose details are shown.</summary>
		public Guid? SelectedGroupId => list.Selected?.Id;

		/// <summary>Raised when the user joins a band (double click, the ✓ button, or Join).</summary>
		public event Action<GroupSummary>? GroupSelected;

		/// <summary>Raised with the name when the user adds a band.</summary>
		public event Action<string>? GroupCreateRequested;

		/// <summary>Raised when an admin deletes the band (after confirming).</summary>
		public event Action<GroupSummary>? GroupDeleteRequested;

		/// <summary>Raised when an admin changes a member's role.</summary>
		public event Action<GroupSummary, GroupMember, GroupRole>? MemberRoleChangeRequested;

		/// <summary>Raised with the new name when an admin changes another member's name.</summary>
		public event Action<GroupSummary, GroupMember, string>? MemberRenameRequested;

		/// <summary>Raised when an admin removes a member (after confirming).</summary>
		public event Action<GroupSummary, GroupMember>? MemberRemoveRequested;

		/// <summary>
		/// Invites whoever has this username, email or phone to the band; returns why it couldn't (e.g.
		/// no such member), or null when they're invited.
		/// </summary>
		public Func<GroupSummary, string, Task<string?>>? InviteRequested { get; set; }

		/// <param name="members">Each band's members, keyed by group id.</param>
		/// <param name="selectedGroupId">The band to show selected (e.g. the one just managed); else the joined one.</param>
		public GroupsView(IReadOnlyList<GroupSummary> groups, IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members, Guid? currentGroupId, Guid? selectedGroupId = null)
		{
			this.members = members;
			this.currentGroupId = currentGroupId;
			var list = new SelectableList<GroupSummary>(
				groups,
				group => GroupContent(group, joined: group.Id == currentGroupId),
				group => group.Id == currentGroupId,
				group => group.Id == currentGroupId ? [] : [JoinButton(group)],
				group => GroupSelected?.Invoke(group),
				"You're not a member of a band yet. Add one with +, or ask a band admin to invite you.");
			list.SelectionChanged += ShowDetails;
			this.list = list;
			if (groups.FirstOrDefault(group => group.Id == selectedGroupId) is { } selected)
			{
				list.Select(selected);
			}

			ShowDetails(list.Selected ?? groups.FirstOrDefault());

			// "+" reveals a name box above the list; Add (or Enter) creates the band.
			var (addButton, nameRow) = PageLayout.AddByName("Add band", "Name of the new band", name => GroupCreateRequested?.Invoke(name));

			// The details and the list scroll together (the details can be long in a big band).
			var column = new StackPanel { Spacing = 16, Children = { nameRow, details, list } };
			Content = PageLayout.Page(
				"Bands",
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				subtitle: "Choose a band",
				titleAction: addButton,
				closable: true);
		}

		private IReadOnlyList<GroupMember> MembersOf(GroupSummary group) => members.GetValueOrDefault(group.Id) ?? [];

		/// <summary>✓: join the band (make it the current one).</summary>
		private Button JoinButton(GroupSummary group)
		{
			var button = HomeView.IconButton(CheckIcon, $"Join {group.Name}");
			button.Click += (_, _) => GroupSelected?.Invoke(group);
			return button;
		}

		/// <summary>The selected band's details (nothing when there's no band).</summary>
		private void ShowDetails(GroupSummary? group)
		{
			details.Content = group == null ? null : Details(group);
		}

		private Control Details(GroupSummary group)
		{
			var bandMembers = MembersOf(group);
			var me = bandMembers.FirstOrDefault(member => member.IsMe);
			var isAdmin = me?.Role == GroupRole.Admin;
			var joined = group.Id == currentGroupId;
			var admins = bandMembers.Count(member => member.Role == GroupRole.Admin);

			// The band's name and the member's place in it; Join and (admins) delete at the right.
			var status = new List<string> { joined ? "Joined" : "Not joined" };
			if (me != null)
			{
				status.Add(me.Approved ? $"You're {Article(me.Role)} {RoleName(me.Role)}" : "You're invited");
			}

			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			if (!joined)
			{
				var join = new Button { Content = me is { Approved: false } ? "Accept and join" : "Join", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
				join.Click += (_, _) => GroupSelected?.Invoke(group);
				actions.Children.Add(join);
			}

			if (isAdmin)
			{
				actions.Children.Add(PageLayout.DeleteButton(
					$"Delete {group.Name}",
					$"Delete the band {group.Name}? Its members, setlists and songs go with it.",
					() => GroupDeleteRequested?.Invoke(group)));
			}

			DockPanel.SetDock(actions, Dock.Right);
			var header = new DockPanel
			{
				Children =
				{
					actions,
					new StackPanel
					{
						Spacing = 2,
						Children =
						{
							new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
							new TextBlock { Text = string.Join(" · ", status), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
						},
					},
				},
			};

			var memberRows = new StackPanel { Spacing = 6 };
			foreach (var member in bandMembers)
			{
				memberRows.Children.Add(MemberRow(group, member, isAdmin, lastAdmin: member.Role == GroupRole.Admin && admins <= 1));
			}

			var panel = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					header,
					new TextBlock { Text = $"Members ({bandMembers.Count})", FontWeight = FontWeight.SemiBold },
					memberRows,
				},
			};

			if (isAdmin)
			{
				panel.Children.Add(InviteRow(group));
			}

			var card = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Child = panel };
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		/// <summary>
		/// A member: name (and "invited"), last login, and the role, which an admin can change; an
		/// admin can also remove them. The last admin can't be demoted or removed.
		/// </summary>
		private Control MemberRow(GroupSummary group, GroupMember member, bool isAdmin, bool lastAdmin)
		{
			var name = member.IsMe ? $"{member.Name} (you)" : member.Name;
			var info = new StackPanel
			{
				Spacing = 0,
				VerticalAlignment = VerticalAlignment.Center,
				Children =
				{
					new TextBlock { Text = name, FontSize = 15, Opacity = member.Approved ? 1 : 0.7, TextTrimming = TextTrimming.CharacterEllipsis },
					StatusLine(member),
				},
			};

			var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			if (isAdmin)
			{
				// The pencil: another member's name (their own isn't an admin's matter).
				tools.Children.Add(member.IsMe ? new Border { Width = 40 } : EditButton(group, member));

				var role = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !lastAdmin };
				foreach (var choice in Enum.GetValues<GroupRole>())
				{
					role.Items.Add(new ComboBoxItem { Content = Capitalized(RoleName(choice)), Tag = choice });
				}

				role.SelectedIndex = (int)member.Role;
				if (lastAdmin)
				{
					ToolTip.SetTip(role, "The band's only admin stays one; make someone else admin first.");
				}

				role.SelectionChanged += (_, _) =>
				{
					if (role.SelectedItem is ComboBoxItem { Tag: GroupRole chosen } && chosen != member.Role)
					{
						MemberRoleChangeRequested?.Invoke(group, member, chosen);
					}
				};
				tools.Children.Add(role);

				if (!member.IsMe && !lastAdmin)
				{
					tools.Children.Add(RemoveButton(group, member));
				}
				else
				{
					// Keeps the role boxes lined up.
					tools.Children.Add(new Border { Width = 40 });
				}
			}
			else
			{
				tools.Children.Add(new TextBlock { Text = Capitalized(RoleName(member.Role)), Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
			}

			DockPanel.SetDock(tools, Dock.Right);
			return new DockPanel { Children = { tools, info } };
		}

		/// <summary>A pencil that opens a small popup to change the member's name: a box with Save (or Enter) and Cancel.</summary>
		private Button EditButton(GroupSummary group, GroupMember member)
		{
			var button = PageLayout.EditButton($"Edit {member.Name}", () => { });
			var nameBox = new TextBox { Text = member.Name, PlaceholderText = "Name", MaxLength = 100, MinWidth = 220 };
			var save = new Button { Content = "Save" };
			save.Classes.Add("accent");
			var cancel = new Button { Content = "Cancel" };
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = "Name", FontSize = 13, Opacity = 0.75 },
						nameBox,
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } },
					},
				},
			};

			void Save()
			{
				var name = nameBox.Text?.Trim() ?? string.Empty;
				if (name.Length == 0)
				{
					return;
				}

				flyout.Hide();
				if (name != member.Name)
				{
					MemberRenameRequested?.Invoke(group, member, name);
				}
			}

			nameBox.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
			nameBox.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					Save();
				}
			};
			save.Click += (_, _) => Save();
			cancel.Click += (_, _) => flyout.Hide();
			// Opened again after a cancel: the name as it is.
			flyout.Opened += (_, _) =>
			{
				nameBox.Text = member.Name;
				nameBox.Focus();
				nameBox.SelectAll();
			};
			button.Flyout = flyout;
			return button;
		}

		/// <summary>✕ that asks first, then removes the member from the band.</summary>
		private Button RemoveButton(GroupSummary group, GroupMember member)
		{
			var button = HomeView.IconButton(RemoveIcon, $"Remove {member.Name}");
			var confirm = new Button { Content = "Remove", Foreground = Brushes.White };
			confirm.Bind(Button.BackgroundProperty, confirm.GetResourceObservable(LyriousTheme.ErrorBrush));
			var cancel = new Button { Content = "Cancel" };
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = $"Remove {member.Name} from {group.Name}?", TextWrapping = TextWrapping.Wrap },
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } },
					},
				},
			};
			confirm.Click += (_, _) =>
			{
				flyout.Hide();
				MemberRemoveRequested?.Invoke(group, member);
			};
			cancel.Click += (_, _) => flyout.Hide();
			button.Flyout = flyout;
			return button;
		}

		/// <summary>A box for a username, email or phone, and Invite (or Enter). Problems show below it.</summary>
		private Control InviteRow(GroupSummary group)
		{
			var box = new TextBox { PlaceholderText = "Username, email or phone", VerticalAlignment = VerticalAlignment.Center };
			var invite = new Button { Content = "Invite", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
			var problem = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));

			async void Invite()
			{
				var query = box.Text?.Trim() ?? "";
				if (query.Length == 0 || InviteRequested == null)
				{
					return;
				}

				invite.IsEnabled = box.IsEnabled = false;
				problem.IsVisible = false;
				try
				{
					// On success the page is shown again, with the new member in the list.
					if (await InviteRequested(group, query) is { } why)
					{
						problem.Text = why;
						problem.IsVisible = true;
					}
				}
				finally
				{
					invite.IsEnabled = box.IsEnabled = true;
				}
			}

			invite.Click += (_, _) => Invite();
			box.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					Invite();
				}
			};

			DockPanel.SetDock(invite, Dock.Right);
			return new StackPanel
			{
				Spacing = 6,
				Children =
				{
					new TextBlock { Text = "Invite a member", FontWeight = FontWeight.SemiBold },
					new DockPanel { Children = { invite, box } },
					problem,
				},
			};
		}

		/// <summary>
		/// Where the member stands: invited (not accepted yet), accepted (in the band, but working in
		/// another one), or joined (this is their current band); connected (in green) while their app
		/// is open and online; then their last login.
		/// </summary>
		private static Control StatusLine(GroupMember member)
		{
			var status = !member.Approved ? "Invited" : member.Joined ? "Joined" : "Accepted";
			var line = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
			line.Inlines!.Add(new Avalonia.Controls.Documents.Run(status) { FontWeight = FontWeight.SemiBold });
			if (member.Connected)
			{
				var connected = new Avalonia.Controls.Documents.Run(" · ● Connected");
				connected.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, line.GetResourceObservable(LyriousTheme.SuccessBrush));
				line.Inlines.Add(connected);
			}

			line.Inlines.Add(new Avalonia.Controls.Documents.Run($" · {LastLoginText(member.LastLogin)}"));
			line.Opacity = 0.85;
			return line;
		}

		/// <summary>E.g. "Last login today 14:05", "yesterday 09:12", "3 days ago" or "12 Nov 2026".</summary>
		private static string LastLoginText(DateTime? lastLogin)
		{
			if (lastLogin is not { } time)
			{
				return "No login recorded yet";
			}

			var local = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
			var days = (DateTime.Today - local.Date).Days;
			var when = days switch
			{
				<= 0 => $"today {local:HH:mm}",
				1 => $"yesterday {local:HH:mm}",
				< 7 => $"{days} days ago",
				_ => local.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
			};
			return $"Last login {when}";
		}

		private static string RoleName(GroupRole role) => role.ToString().ToLowerInvariant();

		private static string Article(GroupRole role) => role == GroupRole.Admin ? "an" : "a";

		private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

		/// <summary>One band: its name; the joined band has a ✓ at the right.</summary>
		private static Control GroupContent(GroupSummary group, bool joined)
		{
			var details = new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
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
	}
}
