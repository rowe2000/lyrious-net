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

		/// <summary>
		/// Makes an account for someone new (username, name, email or phone, language) and invites them to the band;
		/// returns why it couldn't (e.g. the username is taken), or null when it's done.
		/// </summary>
		public Func<GroupSummary, string, string, string, string, Task<string?>>? CreateMemberRequested { get; set; }

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

			// The other bands above the details, flowing left to right; a double-click on one joins it.
			otherBands.IsVisible = groups.Count > 1 && !singleBand;
			otherBands.AddHandler(PointerPressedEvent, OnBandsPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
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
				var button = new Button { Content = GroupContent(band, joined: band.Id == currentGroupId), Padding = new Thickness(12, 6), Tag = band };
				ToolTip.SetTip(button, L.T("Click to show it, double-click to join it"));
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

		/// <summary>
		/// A double-click on a band's button (see the constructor): joins it, as Join does (accepting an
		/// invitation), when the member is in it and hasn't joined it already.
		/// </summary>
		private void OnBandsPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
		{
			// The second press of a double-click (the first showed the band, making the buttons anew).
			if (e.ClickCount != 2 || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(e.Source as Avalonia.Visual, includeSelf: true) is not { Tag: GroupSummary band })
			{
				return;
			}

			if (band.Id != currentGroupId && MembersOf(band).Any(member => member.IsMe))
			{
				e.Handled = true;
				GroupSelected?.Invoke(band);
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
				// The note of the joined band (♩) before the text: joining makes it the current one.
				var joinNote = PageLayout.Note();
				joinNote.Margin = new Thickness(-4, -4, 4, -4);
				var join = new Button
				{
					Content = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Children =
						{
							joinNote,
							new TextBlock { Text = me is { Approved: false } ? L.T("Accept and join") : L.T("Join"), VerticalAlignment = VerticalAlignment.Center },
						},
					},
					Classes = { "accent" },
					VerticalAlignment = VerticalAlignment.Center,
					Height = 40,
					VerticalContentAlignment = VerticalAlignment.Center,
				};
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
				var inviteRow = InviteRow(group);
				membersTab.Children.Add(inviteRow);
				membersTab.Children.Add(CreateMemberRow(group, inviteRow));
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
			var name = member.IsMe ? L.T("{0} (you)", member.Name) : member.Name;
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

			// The name and status, or the box to change the name in while it's edited (see EditButton).
			var infoHost = new ContentControl { Content = info, VerticalAlignment = VerticalAlignment.Center };

			var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			if (isAdmin)
			{
				// The pencil: another member's name (their own isn't an admin's matter).
				// (A gap of its size for the member themselves, so the role boxes line up.)
				Control edit = member.IsMe ? new Border { Width = 32 } : EditButton(group, member, infoHost, info);
				tools.Children.Add(edit);

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

				// The ✕ to kick them out. Where there's none, a gap of its size keeps the role boxes lined up.
				Control kickOut = !member.IsMe && !lastAdmin
					? RemoveButton(group, member)
					: new Border { Width = 32 };
				tools.Children.Add(kickOut);

				// The pencil and the ✕ (or their gaps): as tall as the role box between them, and square.
				foreach (var button in new[] { edit, kickOut }.OfType<Button>())
				{
					button.Padding = new Thickness(0);
					button.MinWidth = 0;
					button.MinHeight = 0;
					button.HorizontalContentAlignment = HorizontalAlignment.Center;
					button.VerticalContentAlignment = VerticalAlignment.Center;
				}

				role.SizeChanged += (_, e) =>
				{
					if (e.HeightChanged)
					{
						foreach (var square in new[] { edit, kickOut })
						{
							square.Width = e.NewSize.Height;
							square.Height = e.NewSize.Height;
						}
					}
				};
			}
			else
			{
				tools.Children.Add(new TextBlock { Text = Capitalized(RoleName(member.Role)), Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
			}

			DockPanel.SetDock(tools, Dock.Right);
			return new DockPanel { Children = { tools, infoHost } };
		}

		/// <summary>
		/// A pencil that changes the member's name in place: a box with their name takes the place of
		/// the name, status and last login, with ✓ in it once the name is changed (or Enter). Everything
		/// else on the page is disabled meanwhile; a click outside the box, or Escape, cancels.
		/// </summary>
		/// <param name="infoHost">Where the name and status show, and the box while editing.</param>
		/// <param name="info">The name and status, shown again when editing ends.</param>
		private Button EditButton(GroupSummary group, GroupMember member, ContentControl infoHost, Control info)
		{
			Button? button = null;
			button = PageLayout.EditButton(L.T("Edit {0}", member.Name), () => EditName(group, member, infoHost, info, button!));
			return button;
		}

		/// <summary>Shows the box to change <paramref name="member"/>'s name in <paramref name="infoHost"/> (see <see cref="EditButton"/>).</summary>
		/// <param name="pencil">The pencil that started it: out of sight while editing (its room kept, so the role box stays put).</param>
		private void EditName(GroupSummary group, GroupMember member, ContentControl infoHost, Control info, Control pencil)
		{
			pencil.Opacity = 0;
			var nameBox = new TextBox { Text = member.Name, PlaceholderText = L.T("Name"), MaxLength = 100, VerticalAlignment = VerticalAlignment.Center };
			// The ✓ in the box, once the name is changed.
			var save = PageLayout.OkButton(L.T("Save the name"));
			PageLayout.OkInBox(nameBox, save, () => !string.IsNullOrWhiteSpace(nameBox.Text) && nameBox.Text.Trim() != member.Name);

			// Everything else on the page is disabled (not hidden) while editing: the siblings of the box
			// and of each panel it's in, up to the page. Those already disabled stay so afterwards.
			var disabled = new List<Control>();
			for (Control? at = infoHost; at != null && at != this; at = at.Parent as Control)
			{
				if (at.Parent is Panel panel)
				{
					foreach (var sibling in panel.Children)
					{
						if (sibling != at && sibling.IsEnabled)
						{
							sibling.IsEnabled = false;
							disabled.Add(sibling);
						}
					}
				}
			}

			var top = TopLevel.GetTopLevel(this);
			var done = false;

			void Finish(bool keep)
			{
				if (done)
				{
					return;
				}

				done = true;
				top?.RemoveHandler(PointerPressedEvent, OnPressed);
				foreach (var control in disabled)
				{
					control.IsEnabled = true;
				}

				infoHost.Content = info;
				pencil.Opacity = 1;
				var name = nameBox.Text?.Trim() ?? string.Empty;
				if (keep && name.Length > 0 && name != member.Name)
				{
					MemberRenameRequested?.Invoke(group, member, name);
				}
			}

			// A press anywhere but in the box (its ✓ included) cancels.
			void OnPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
			{
				if (e.Source is not Visual pressed || (pressed != nameBox && !Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(nameBox, pressed)))
				{
					Finish(keep: false);
				}
			}

			nameBox.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					Finish(keep: true);
				}
				else if (e.Key == Avalonia.Input.Key.Escape)
				{
					e.Handled = true;
					Finish(keep: false);
				}
			};
			save.Click += (_, _) => Finish(keep: true);
			// The page shown anew (or closed) meanwhile: only the watch on presses is left to remove.
			nameBox.DetachedFromVisualTree += (_, _) => Finish(keep: false);

			infoHost.Content = nameBox;
			top?.AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
			Avalonia.Threading.Dispatcher.UIThread.Post(() => nameBox.Focus());
		}

		/// <summary>✕ that asks first, then removes the member from the band.</summary>
		private Button RemoveButton(GroupSummary group, GroupMember member)
		{
			var button = HomeView.IconButton(RemoveIcon, L.T("Kick out {0}", member.Name));
			// Red when pointed at, as the trash buttons: it removes someone from the band.
			PageLayout.RedOnHover(button);
			var confirm = PageLayout.DangerButton(L.T("Kick out"));
			var cancel = new Button { Content = L.T("Cancel") };
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = L.T("Kick {0} out of {1}?", member.Name, group.Name), TextWrapping = TextWrapping.Wrap },
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

		/// <summary>A box for a username, email or phone, with Invite in it (or Enter). Problems show below it.</summary>
		private Control InviteRow(GroupSummary group)
		{
			var box = new TextBox { PlaceholderText = L.T("Username, email or phone"), VerticalAlignment = VerticalAlignment.Center };
			// Invite in the box, at its right end: highlighted, as tall as the box; usable once something's typed.
			var invite = new Button { Content = L.T("Invite"), Classes = { "accent" }, IsEnabled = false };
			PageLayout.FitInBox(box, invite);
			box.InnerRightContent = invite;
			box.TextChanged += (_, _) => invite.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
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
					box.IsEnabled = true;
					invite.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
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

			return new StackPanel
			{
				Spacing = 6,
				Children =
				{
					new TextBlock { Text = L.T("Invite a member"), FontWeight = FontWeight.SemiBold },
					box,
					problem,
				},
			};
		}

		/// <summary>
		/// "+ Create a member…" for someone who has no account yet: opens their name, a username (made
		/// from the name until it's changed) and their email or phone number, with "Create and invite"
		/// and a ✕ (or Escape) that closes it; the invite box is hidden meanwhile. They're sent their
		/// username and how to set their own password, by email or SMS. Problems show below it.
		/// </summary>
		/// <param name="inviteRow">The invite box, hidden while a member is being created.</param>
		private Control CreateMemberRow(GroupSummary group, Control inviteRow)
		{
			var open = new Button
			{
				Content = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 6,
					Children = { SongsView.LineIcon(PageLayout.AddIcon, 16), new TextBlock { Text = L.T("Create a member…"), VerticalAlignment = VerticalAlignment.Center } },
				},
				HorizontalAlignment = HorizontalAlignment.Left,
			};
			ToolTip.SetTip(open, L.T("Makes an account for someone who has none yet, and invites them to the band. They're sent their username and how to set their own password, by email or SMS."));
			var name = new TextBox { PlaceholderText = L.T("Their name") };
			var userName = new TextBox { PlaceholderText = L.T("Username") };
			var email = new TextBox { PlaceholderText = L.T("Their email or phone number") };
			// Their language: the admin's own to start with.
			var language = Localization.Language.Current;
			var languageChoice = PageLayout.LanguageChoice(language, code => language = code);
			var create = new Button { Content = L.T("Create and invite"), Classes = { "accent" }, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left };
			// The ✕ at the heading's right; Escape (once out of the boxes) closes it too, before the page's ✕.
			var close = EscapeKey.MarkAsClose(PageLayout.CancelButton(L.T("Close")), first: true);
			close.VerticalAlignment = VerticalAlignment.Center;
			DockPanel.SetDock(close, Dock.Right);
			var problem = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));
			var form = new StackPanel
			{
				Spacing = 6,
				IsVisible = false,
				Children =
				{
					new DockPanel { Children = { close, new TextBlock { Text = L.T("Create a member"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } },
					name,
					userName,
					email,
					languageChoice,
					create,
					problem,
				},
			};

			// The username follows the name (its letters and digits, in lower case) until it's typed in itself.
			var userNameTyped = false;
			var suggesting = false;
			name.TextChanged += (_, _) =>
			{
				if (!userNameTyped)
				{
					suggesting = true;
					userName.Text = new string((name.Text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
					suggesting = false;
				}
			};
			userName.TextChanged += (_, _) => userNameTyped |= !suggesting;

			// An email address (with an @) or a phone number (at least 8 digits); the server checks it properly.
			void Check() => create.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(userName.Text)
				&& ((email.Text ?? "").Contains('@') || (email.Text ?? "").Count(char.IsDigit) >= 8);
			name.TextChanged += (_, _) => Check();
			userName.TextChanged += (_, _) => Check();
			email.TextChanged += (_, _) => Check();

			void Show(bool shown)
			{
				form.IsVisible = shown;
				open.IsVisible = inviteRow.IsVisible = !shown;
				problem.IsVisible = false;
				if (shown)
				{
					name.Focus();
				}
				else
				{
					// Closed: the next one starts empty, the username following the name again.
					name.Text = userName.Text = email.Text = "";
					userNameTyped = false;
				}
			}

			async void Create()
			{
				if (!create.IsEnabled || CreateMemberRequested == null)
				{
					return;
				}

				create.IsEnabled = name.IsEnabled = userName.IsEnabled = email.IsEnabled = false;
				problem.IsVisible = false;
				try
				{
					// On success the page is shown again, with the new member in the list as invited.
					if (await CreateMemberRequested(group, userName.Text!.Trim(), name.Text!.Trim(), email.Text!.Trim(), language) is { } why)
					{
						problem.Text = why;
						problem.IsVisible = true;
					}
				}
				finally
				{
					name.IsEnabled = userName.IsEnabled = email.IsEnabled = true;
					Check();
				}
			}

			open.Click += (_, _) => Show(true);
			close.Click += (_, _) => Show(false);
			create.Click += (_, _) => Create();
			foreach (var box in new[] { name, userName, email })
			{
				box.KeyDown += (_, e) =>
				{
					if (e.Key == Avalonia.Input.Key.Enter)
					{
						e.Handled = true;
						Create();
					}
				};
			}

			return new StackPanel { Spacing = 6, Children = { open, form } };
		}

		/// <summary>
		/// Where the member stands: invited (not accepted yet), accepted (in the band, but working in
		/// another one), or joined (this is their current band); connected (in green) while their app
		/// is open and online (then "joined" or "accepted" isn't shown); then their last login. Joined
		/// but not connected is in the text colour, the rest gray, as the member badges.
		/// </summary>
		private static Control StatusLine(GroupMember member)
		{
			var line = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
			// A connected member in the band is just "● Connected" (no "Joined" or "Accepted").
			var showStatus = !(member.Connected && member.Approved);
			if (showStatus)
			{
				var status = !member.Approved ? L.T("Invited") : member.Joined ? L.T("Joined") : L.T("Accepted");
				// Full text colour when joined, else gray: the theme's dimmed text colour.
				var statusRun = new Avalonia.Controls.Documents.Run(status) { FontWeight = FontWeight.SemiBold };
				if (!(member.Approved && member.Joined))
				{
					statusRun.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, line.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
				}

				line.Inlines!.Add(statusRun);
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
