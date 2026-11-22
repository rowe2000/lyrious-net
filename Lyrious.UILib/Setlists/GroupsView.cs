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
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// One band's details (at first the joined one), and all the member's other bands as buttons
	/// above them, flowing left to right; clicking one shows its details instead (the joined band is
	/// highlighted and has a ♩).
	/// The details: who's in it (role, invited or not, last login), Join, and for the band's admins
	/// tools to invite, set roles, remove members and delete the band.
	/// </summary>
	public sealed class GroupsView : UserControl
	{
		private const string RemoveIcon = "M6,6 L18,18 M18,6 L6,18";

		private readonly IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members;
		private readonly Guid? currentGroupId;
		private readonly ContentControl details = new();
		private readonly IReadOnlyList<GroupSummary> groups;
		private readonly WrapPanel otherBands = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
		private GroupSummary? shownGroup;

		/// <summary>The band whose details are shown.</summary>
		public Guid? SelectedGroupId => shownGroup?.Id;

		/// <summary>The band shown as the joined one (when the page was made).</summary>
		public Guid? CurrentGroupId => currentGroupId;

		/// <summary>Raised when the user joins a band (Join).</summary>
		public event Action<GroupSummary>? GroupSelected;

		/// <summary>Raised with the name when the user adds a band.</summary>
		public event Action<string>? GroupCreateRequested;

		/// <summary>Raised with the new name when the user renames a band (clicking its name).</summary>
		public event Action<GroupSummary, string>? GroupRenameRequested;

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
		/// <param name="singleBand">Only the selected band: no list of the other bands, and no adding one.</param>
		/// <param name="embedded">Only the selected band's members (their rows and inviting), to put inside another page: no page, header or card.</param>
		public GroupsView(IReadOnlyList<GroupSummary> groups, IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> members, Guid? currentGroupId, Guid? selectedGroupId = null, bool singleBand = false, bool embedded = false)
		{
			this.embedded = embedded;
			this.members = members;
			this.currentGroupId = currentGroupId;
			this.groups = groups;
			var current = groups.FirstOrDefault(group => group.Id == currentGroupId);
			var shown = groups.FirstOrDefault(group => group.Id == selectedGroupId) ?? current ?? groups.FirstOrDefault();

			// The other bands above the details, flowing left to right.
			otherBands.IsVisible = groups.Count > 1 && !singleBand;
			ShowBand(shown);
			if (embedded)
			{
				Content = details;
				return;
			}

			// "+" reveals a name box above the list; Add (or Enter) creates the band.
			var (addButton, nameRow) = PageLayout.AddByName(L.T("Add band"), L.T("Name of the new band"), name => GroupCreateRequested?.Invoke(name));

			// The other bands and the details scroll together (the details can be long in a big band).
			var column = new StackPanel { Spacing = 16, Children = { nameRow, otherBands, details } };

			Content = PageLayout.Page(
				L.T("Band"),
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				subtitle: current == null ? L.T("Choose a band") : null,
				titleAction: singleBand ? null : addButton,
				closable: true,
				icon: AppIcons.Band);
		}

		private IReadOnlyList<GroupMember> MembersOf(GroupSummary group) => members.GetValueOrDefault(group.Id) ?? [];

		/// <summary>
		/// Shows the band's details (nothing when there's no band), and all the bands (this one
		/// highlighted) as buttons above; clicking another shows that one instead.
		/// </summary>
		private void ShowBand(GroupSummary? group)
		{
			shownGroup = group;
			details.Content = group == null ? null : Details(group);

			otherBands.Children.Clear();
			foreach (var band in groups)
			{
				// As wide as the name (and the joined band's ♩); the one shown below is highlighted.
				var button = new Button { Content = GroupContent(band, joined: band.Id == currentGroupId), Padding = new Thickness(12, 6) };
				if (band.Id == group?.Id)
				{
					button.Classes.Add("accent");
				}
				else
				{
					button.Click += (_, _) => ShowBand(band);
				}

				otherBands.Children.Add(button);
			}
		}

		// Only the members, inside another page (see the constructor).
		private readonly bool embedded;

		private Control Details(GroupSummary group)
		{
			var bandMembers = MembersOf(group);
			var me = bandMembers.FirstOrDefault(member => member.IsMe);
			// The private band has only its member in it, and nothing to manage: no invites, roles or delete.
			// A site manager manages every band but private ones (see IAuthService.IsSiteManager).
			var isAdmin = (me?.Role == GroupRole.Admin || App.AuthService.IsSiteManager) && !group.IsPrivate;
			var joined = group.Id == currentGroupId;
			var admins = bandMembers.Count(member => member.Role == GroupRole.Admin);

			// The band's name and the member's place in it; Join and (admins) delete at the right.
			var status = new List<string> { joined ? "Joined" : L.T("Not joined") };
			if (group.IsPrivate)
			{
				status.Add(L.T("Private · only you"));
			}
			else if (me != null)
			{
				status.Add(me.Approved ? $"You're {Article(me.Role)} {RoleName(me.Role)}" : L.T("You're invited"));
			}

			if (App.AuthService.IsSiteManager && !group.IsPrivate)
			{
				status.Add(L.T("You manage it as site manager"));
			}

			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			// Join only for the band's own members (a site manager looking at another band has none).
			if (!joined && me != null)
			{
				var join = new Button { Content = me is { Approved: false } ? L.T("Accept and join") : "Join", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center, Height = 40, VerticalContentAlignment = VerticalAlignment.Center };
				join.Click += (_, _) => GroupSelected?.Invoke(group);
				actions.Children.Add(join);
			}

			if (isAdmin)
			{
				actions.Children.Add(PageLayout.DeleteButton(
					L.T("Delete {0}", group.Name),
					L.T("Delete the band {0}? Its members, setlists and songs go with it.", group.Name),
					() => GroupDeleteRequested?.Invoke(group)));
			}

			DockPanel.SetDock(actions, Dock.Right);
			// The name (or the box to change it) on one line with the tools; the status below.
			var header = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new DockPanel
					{
						Children =
						{
							actions,
							// The private band's name is fixed; any other band's can be changed by its members.
							group.IsPrivate
								? new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MinHeight = 40, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 6) }
								: PageLayout.EditableTitle(group.Name, name => GroupRenameRequested?.Invoke(group, name)),
						},
					},
					new TextBlock { Text = string.Join(" · ", status), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
				},
			};

			var memberRows = new StackPanel { Spacing = 6 };
			foreach (var member in bandMembers)
			{
				memberRows.Children.Add(MemberRow(group, member, isAdmin, lastAdmin: member.Role == GroupRole.Admin && admins <= 1));
			}

			// The members (and inviting), or what the private band is for.
			var membersTab = new StackPanel { Spacing = 12 };
			if (!group.IsPrivate)
			{
				membersTab.Children.Add(memberRows);
			}
			else
			{
				membersTab.Children.Add(new TextBlock
				{
					Text = L.T("This is your private place to keep your own songs and setlists. No one else can see them, and no one can be invited here."),
					TextWrapping = TextWrapping.Wrap,
					Opacity = 0.85,
				});
			}

			if (isAdmin)
			{
				membersTab.Children.Add(InviteRow(group));
			}

			if (embedded)
			{
				return membersTab;
			}

			var panel = new StackPanel { Spacing = 12, Children = { header } };

			// The members (the MIDI settings are each member's own, in Settings).
			panel.Children.Add(membersTab);

			var card = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Child = panel };
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			if (joined)
			{
				// The joined band's card is outlined in the accent colour, like its button.
				card.BorderThickness = new Thickness(2);
				card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("AccentButtonBackground"));
			}

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
					ToolTip.SetTip(role, L.T("The band's only admin stays one; make someone else admin first."));
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

		/// <summary>A pencil that opens a small popup to change the member's name: a box with ✓ (or Enter) and ✕.</summary>
		private Button EditButton(GroupSummary group, GroupMember member)
		{
			var button = PageLayout.EditButton(L.T("Edit {0}", member.Name), () => { });
			var nameBox = new TextBox { Text = member.Name, PlaceholderText = L.T("Name"), MaxLength = 100, MinWidth = 220 };
			var save = PageLayout.OkButton(L.T("Save the name"));
			var cancel = PageLayout.CancelButton(L.T("Cancel"));
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = L.T("Name"), FontSize = 13, Opacity = 0.75 },
						nameBox,
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { save, cancel } },
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
			var button = HomeView.IconButton(RemoveIcon, L.T("Remove {0}", member.Name));
			var confirm = PageLayout.DangerButton(L.T("Remove"));
			var cancel = new Button { Content = L.T("Cancel") };
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = L.T("Remove {0} from {1}?", member.Name, group.Name), TextWrapping = TextWrapping.Wrap },
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
			var box = new TextBox { PlaceholderText = L.T("Username, email or phone"), VerticalAlignment = VerticalAlignment.Center };
			var invite = new Button { Content = L.T("Invite"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
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
					new TextBlock { Text = L.T("Invite a member"), FontWeight = FontWeight.SemiBold },
					new DockPanel { Children = { invite, box } },
					problem,
				},
			};
		}

		/// <summary>
		/// Where the member stands: invited (not accepted yet), accepted (in the band, but working in
		/// another one), or joined (this is their current band); connected (in green) while their app
		/// is open and online (then "joined" or "accepted" isn't shown); then their last login.
		/// </summary>
		private static Control StatusLine(GroupMember member)
		{
			var line = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
			// A connected member in the band is just "● Connected" (no "Joined" or "Accepted").
			var showStatus = !(member.Connected && member.Approved);
			if (showStatus)
			{
				var status = !member.Approved ? L.T("Invited") : member.Joined ? L.T("Joined") : L.T("Accepted");
				line.Inlines!.Add(new Avalonia.Controls.Documents.Run(status) { FontWeight = FontWeight.SemiBold });
			}

			if (member.Connected)
			{
				var connected = new Avalonia.Controls.Documents.Run(showStatus ? " · ● Connected" : "● Connected");
				connected.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, line.GetResourceObservable(LyriousTheme.SuccessBrush));
				line.Inlines!.Add(connected);
			}

			line.Inlines!.Add(new Avalonia.Controls.Documents.Run($" · {LastLoginText(member.LastLogin)}"));
			line.Opacity = 0.85;
			return line;
		}

		/// <summary>E.g. "Last login today 14:05", "yesterday 09:12", "3 days ago" or "12 Nov 2026".</summary>
		private static string LastLoginText(DateTime? lastLogin)
		{
			if (lastLogin is not { } time)
			{
				return L.T("No login recorded yet");
			}

			var local = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
			var days = (DateTime.Today - local.Date).Days;
			var when = days switch
			{
				<= 0 => $"today {local:HH:mm}",
				1 => $"yesterday {local:HH:mm}",
				< 7 => L.T("{0} days ago", days),
				_ => local.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
			};
			return L.T("Last login {0}", when);
		}

		private static string RoleName(GroupRole role) => role.ToString().ToLowerInvariant();

		private static string Article(GroupRole role) => role == GroupRole.Admin ? "an" : "a";

		private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

		/// <summary>One band: its name; the joined band has the note of the current one (♩) at the right.</summary>
		private static Control GroupContent(GroupSummary group, bool joined)
		{
			var details = new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
			if (!joined)
			{
				return details;
			}

			// In the button's text colour, so it shows on the highlight too.
			var note = PageLayout.CurrentCheck(L.T("Joined"));
			return new DockPanel { Children = { note, details } };
		}
	}
}
