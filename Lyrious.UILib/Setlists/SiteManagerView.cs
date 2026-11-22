using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The site manager's page (left drawer → Site manager, for a member the server names as one, see
	/// <see cref="Login.IAuthService.IsSiteManager"/>): every band and every member.
	/// <list type="bullet">
	/// <item>A band: its name (changed here), its members (the band page for that band alone, as its
	/// admin), and its band settings, here.</item>
	/// <item>A member: their name (changed here), username, bands and last login, and their own settings
	/// (e.g. the colours), changed here as JSON.</item>
	/// </list>
	/// Other members' private bands aren't shown: no one else sees those.
	/// </summary>
	public sealed class SiteManagerView : UserControl
	{
		private readonly ISetlistService service;
		private readonly Func<Guid, ContentControl, Task> showBandMembers;
		private readonly Func<Guid, ContentControl, Task> showBandSettings;
		private readonly StackPanel bands = new() { Spacing = 6 };
		private readonly StackPanel members = new() { Spacing = 6 };
		// The tabs' headers, with the number of bands and members the search shows.
		private readonly TextBlock bandsTitle = new() { FontSize = 16 };
		private readonly TextBlock membersTitle = new() { FontSize = 16 };
		private readonly TextBox filter = new() { PlaceholderText = L.T("Search bands and members"), MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
		private IReadOnlyList<GroupSummary> allBands = [];
		private IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> bandMembers = new Dictionary<Guid, IReadOnlyList<GroupMember>>();
		private IReadOnlyList<SiteMember> allMembers = [];
		// Each band's songs, setlists and sessions (and the last session's date), for its details.
		private Dictionary<Guid, BandContent> bandContent = [];

		// Closes the band shown with its Members or Settings open: only one band at a time is.
		private Action? closeOpenBand;

		// Puts each band and member shown back to its details (Members, Settings, User settings closed).
		private readonly List<Action> resetRows = [];

		private sealed record BandContent(int Songs, int Setlists, int Sessions, DateTime? LastSession);

		/// <param name="showBandMembers">Puts the band's members (their roles, inviting) in the place given.</param>
		/// <param name="showBandSettings">Puts the band's settings in the place given.</param>
		public SiteManagerView(ISetlistService service, Func<Guid, ContentControl, Task> showBandMembers, Func<Guid, ContentControl, Task> showBandSettings)
		{
			this.service = service;
			this.showBandMembers = showBandMembers;
			this.showBandSettings = showBandSettings;

			filter.TextChanged += (_, _) => Show();
			// A magnifying glass at the left in the search box, dimmed like its placeholder.
			var searchIcon = SongsView.LineIcon(SongsView.SearchIcon, 16);
			searchIcon.Margin = new Thickness(10, 0, 0, 0);
			searchIcon.Opacity = 0.6;
			searchIcon.IsHitTestVisible = false;
			filter.InnerLeftContent = searchIcon;

			// The intro and the search above two tabs, Bands and Members (with how many the search
			// shows); each tab's list scrolls on its own.
			var top = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					new TextBlock
					{
						Text = L.T("As site manager you can see and change every band and member: rename bands, change their members and settings, and change members' names and settings. Other members' private bands stay private."),
						TextWrapping = TextWrapping.Wrap,
						Opacity = 0.75,
					},
					filter,
				},
			};
			DockPanel.SetDock(top, Dock.Top);
			static ScrollViewer Scrolling(Control list) => new()
			{
				Content = new Border { Child = list, Margin = new Thickness(0, 8, 0, 16) },
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
			};
			var tabs = new TabControl
			{
				// Another tab: its bands and members are shown with their details again.
				Margin = new Thickness(0, 8, 0, 0),
				ItemsSource = new[]
				{
					new TabItem { Header = bandsTitle, Content = Scrolling(bands) },
					new TabItem { Header = membersTitle, Content = Scrolling(members) },
				},
			};
			tabs.SelectionChanged += (_, e) =>
			{
				if (e.Source == tabs)
				{
					foreach (var reset in resetRows)
					{
						reset();
					}
				}
			};
			Content = PageLayout.Page(L.T("Site manager"), new DockPanel { Children = { top, tabs } }, closable: true, icon: AppIcons.SiteManager);
			_ = LoadAsync();
		}

		/// <summary>Shown: the cursor in the search box, to type straight away.</summary>
		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			Avalonia.Threading.Dispatcher.UIThread.Post(() => filter.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
		}

		private async Task LoadAsync()
		{
			try
			{
				allBands = (await service.GetGroupsAsync()).OrderBy(band => band.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
				bandMembers = await service.GetGroupMembersAsync();
				allMembers = await service.GetAllMembersAsync();
				bandContent = [];
				foreach (var band in allBands)
				{
					var songs = await service.GetSongbookSongsAsync(band.Id);
					var setlists = await service.GetSetlistsAsync(band.Id);
					var sessions = await service.GetPlaylogsAsync(band.Id);
					bandContent[band.Id] = new BandContent(songs.Count, setlists.Count, sessions.Count, sessions.Count > 0 ? sessions.Max(session => session.CreatedAt) : null);
				}

				Show();
			}
			catch (Exception ex)
			{
				bands.Children.Clear();
				bands.Children.Add(Message(L.T("Couldn't read the bands and members. {0}", ex.Message), error: true));
			}
		}

		/// <summary>The bands and members that match the search (by name, username or a band's name).</summary>
		private void Show()
		{
			var text = filter.Text?.Trim() ?? "";
			bool Matches(string? value) => text.Length == 0 || (value?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);

			// Each member's bands: the band, and their place in it (role, invited, joined, online).
			var bandsById = allBands.ToDictionary(band => band.Id);
			var membershipsOf = bandMembers
				.SelectMany(entry => entry.Value.Select(member => (Band: bandsById.GetValueOrDefault(entry.Key), Member: member)))
				.Where(entry => entry.Band != null)
				.GroupBy(entry => entry.Member.MemberId)
				.ToDictionary(
					group => group.Key,
					group => (IReadOnlyList<Membership>)group
						.Select(entry => new Membership(entry.Band!, entry.Member))
						.OrderBy(membership => membership.Band.Name, StringComparer.CurrentCultureIgnoreCase)
						.ToList());

			var shownBands = allBands.Where(band => Matches(band.Name)).ToList();
			bandsTitle.Text = L.T("Bands ({0})", shownBands.Count);
			bands.Children.Clear();
			closeOpenBand = null;
			resetRows.Clear();
			foreach (var band in shownBands)
			{
				bands.Children.Add(BandRow(band));
			}

			var shownMembers = allMembers
				.Where(member => Matches(member.Name) || Matches(member.UserName) || (membershipsOf.GetValueOrDefault(member.Id)?.Any(membership => Matches(membership.Band.Name)) ?? false))
				.ToList();
			membersTitle.Text = L.T("Members ({0})", shownMembers.Count);
			members.Children.Clear();
			foreach (var member in shownMembers)
			{
				members.Children.Add(MemberRow(member, membershipsOf.GetValueOrDefault(member.Id) ?? []));
			}
		}

		/// <summary>
		/// A band: its name (click to change) with Members and its settings at the right; below, how many
		/// members, or (Members) its members to manage in place of that, or its settings.
		/// </summary>
		private Control BandRow(GroupSummary band)
		{
			var membersButton = new Button { Content = L.T("Members"), IsVisible = !band.IsPrivate, VerticalAlignment = VerticalAlignment.Center };
			var settingsButton = new Button { Content = L.T("Settings"), VerticalAlignment = VerticalAlignment.Center };
			ToolTip.SetTip(settingsButton, L.T("Band settings"));
			var settings = new ContentControl { IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
			var message = Message("", error: false);
			message.IsVisible = false;
			var below = new StackPanel { Spacing = 2, Children = { BandDetails(band), message, settings } };
			// Members: the band's members in place of everything below the name, until pressed again.
			var membersView = new ContentControl { IsVisible = false, Margin = new Thickness(0, 4, 0, 8) };
			// The button of what's open (Members or Settings) highlighted, like a pressed toggle.
			static void Highlight(Button button, bool on)
			{
				if (on)
				{
					button.Classes.Add("accent");
				}
				else
				{
					button.Classes.Remove("accent");
				}
			}

			void ShowSettings(bool on)
			{
				settings.IsVisible = on;
				Highlight(settingsButton, on);
			}

			void ShowMembers(bool on)
			{
				membersView.IsVisible = on;
				below.IsVisible = !on;
				Highlight(membersButton, on);
				if (on)
				{
					ShowSettings(false);
				}
			}

			// Opening this band's Members or Settings closes the other band's that was open.
			void Close()
			{
				ShowMembers(false);
				ShowSettings(false);
			}

			// The whole band (its members or settings too) scrolled into view, once laid out.
			Control? card = null;
			void FitInView() => Avalonia.Threading.Dispatcher.UIThread.Post(() => card?.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);

			resetRows.Add(() =>
			{
				Close();
				Opened(false);
			});

			void Opened(bool on)
			{
				if (on)
				{
					FitInView();
					if (closeOpenBand != Close)
					{
						closeOpenBand?.Invoke();
					}

					closeOpenBand = Close;
				}
				else if (closeOpenBand == Close)
				{
					closeOpenBand = null;
				}
			}

			membersButton.Click += async (_, _) =>
			{
				var on = !membersView.IsVisible;
				ShowMembers(on);
				Opened(on);
				if (on)
				{
					membersView.Content = new ProgressBar { IsIndeterminate = true, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
					try
					{
						await showBandMembers(band.Id, membersView);
						FitInView();
					}
					catch (Exception ex)
					{
						membersView.Content = Message(L.T("Couldn't read the members. {0}", ex.Message), error: true);
					}
				}
			};

			var loaded = false;
			settingsButton.Click += async (_, _) =>
			{
				ShowMembers(false);
				ShowSettings(!settings.IsVisible);
				Opened(settings.IsVisible);
				if (settings.IsVisible && !loaded)
				{
					loaded = true;
					await showBandSettings(band.Id, settings);
					FitInView();
				}
			};

			// The private band keeps its name (in italics, not to be changed) and has no members to manage.
			Control name = band.IsPrivate
				? new TextBlock { Text = band.Name, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, MinHeight = 40, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 10) }
				: EditableName(band.Name, L.T("Rename band"), message, async newName =>
				{
					await service.RenameGroupAsync(band.Id, newName);
					band = band with { Name = newName };
					allBands = allBands.Select(other => other.Id == band.Id ? band : other).ToList();
				});

			var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { membersButton, settingsButton } };
			DockPanel.SetDock(tools, Dock.Right);
			return card = Card(new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new DockPanel { Children = { tools, name } },
					below,
					membersView,
				},
			});
		}

		/// <summary>A member: their name (changed here), username, bands and last login, and their own settings.</summary>
		/// <summary>A member's place in a band.</summary>
		private sealed record Membership(GroupSummary Band, GroupMember Member);

		private Control MemberRow(SiteMember member, IReadOnlyList<Membership> memberships)
		{
			var message = Message("", error: false);
			message.IsVisible = false;
			var name = EditableName(member.Name, L.T("Change name"), message, async newName =>
			{
				await service.RenameMemberAsync(member.Id, newName);
				member = member with { Name = newName };
			});

			var details = MemberDetails(member, memberships);

			// Their own settings (the colours and the like), as JSON: read when opened, saved with Save.
			var settingsButton = new Button { Content = L.T("User settings"), VerticalAlignment = VerticalAlignment.Center };
			var settingsText = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, FontFamily = FontFamily.Parse("monospace"), PlaceholderText = "{ }" };
			var saveSettings = new Button { Content = L.T("Save settings") };
			var settingsPanel = new StackPanel
			{
				Spacing = 6,
				IsVisible = false,
				Children =
				{
					new TextBlock { Text = L.T("Their own settings, the same on all their devices (e.g. the colours), as a JSON object."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
					settingsText,
					saveSettings,
				},
			};
			resetRows.Add(() =>
			{
				settingsPanel.IsVisible = false;
				details.IsVisible = true;
				settingsButton.Classes.Remove("accent");
			});
			settingsButton.Click += async (_, _) =>
			{
				settingsPanel.IsVisible = !settingsPanel.IsVisible;
				// The details make room for the settings while they're open.
				details.IsVisible = !settingsPanel.IsVisible;
				// Highlighted while open, like the bands' Members and Settings.
				if (settingsPanel.IsVisible)
				{
					settingsButton.Classes.Add("accent");
				}
				else
				{
					settingsButton.Classes.Remove("accent");
				}

				if (!settingsPanel.IsVisible)
				{
					return;
				}

				try
				{
					var json = await service.GetMemberSettingsAsync(member.Id);
					settingsText.Text = json == null ? "" : JsonNode.Parse(json)?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? json;
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't read their settings. {0}", ex.Message), error: true);
				}
			};
			saveSettings.Click += async (_, _) =>
			{
				var text = string.IsNullOrWhiteSpace(settingsText.Text) ? "{}" : settingsText.Text;
				JsonObject? parsed;
				try
				{
					parsed = JsonNode.Parse(text) as JsonObject;
				}
				catch (System.Text.Json.JsonException)
				{
					parsed = null;
				}

				if (parsed == null)
				{
					Show(message, L.T("The settings must be a JSON object, e.g. { \"chord-color\": \"#FDBD00\" }."), error: true);
					return;
				}

				try
				{
					await service.SaveMemberSettingsAsync(member.Id, parsed.ToJsonString());
					Show(message, L.T("Their settings have been saved."), error: false);
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't save their settings. {0}", ex.Message), error: true);
				}
			};

			return Card(new StackPanel
			{
				Spacing = 2,
				Children =
				{
					// The name, with User settings at the right.
					new DockPanel { Children = { Docked(settingsButton, Dock.Right), name } },
					details,
					message,
					settingsPanel,
				},
			});
		}

		/// <summary>
		/// A name shown as a title; a click on it puts it in a text box to change, with a tick beside it
		/// once the text differs. Enter or the tick saves it (<paramref name="save"/>); Escape, focus
		/// moving elsewhere, or leaving it unchanged puts the title back (unsaved text dropped). The row keeps its height either way; how it went shows in <paramref name="message"/>.
		/// </summary>
		private static Control EditableName(string current, string tip, TextBlock message, Func<string, Task> save)
		{
			var title = new TextBlock { Text = current, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
			var shown = new Button
			{
				Content = title,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Padding = new Thickness(0),
				VerticalAlignment = VerticalAlignment.Center,
				Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
			};
			ToolTip.SetTip(shown, tip);
			var box = new TextBox { MinWidth = 200, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center };
			var ok = HomeView.IconButton("M5,12 L10,17 L19,7", L.T("OK"));
			ok.VerticalAlignment = VerticalAlignment.Center;
			ok.IsVisible = false;
			var editor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, IsVisible = false, Children = { box, ok } };
			var saving = false;

			void Edit(bool on)
			{
				shown.IsVisible = !on;
				editor.IsVisible = on;
				if (on)
				{
					box.Text = current;
					ok.IsVisible = false;
					box.SelectAll();
					box.Focus();
				}
			}

			async Task CommitAsync()
			{
				var newName = (box.Text ?? "").Trim();
				if (newName.Length == 0 || newName == current)
				{
					Edit(false);
					return;
				}

				ok.IsEnabled = false;
				saving = true;
				try
				{
					await save(newName);
					current = newName;
					title.Text = newName;
					Edit(false);
					Show(message, L.T("The name has been changed."), error: false);
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't change the name. {0}", ex.Message), error: true);
					box.Focus();
				}

				saving = false;
				ok.IsEnabled = true;
			}

			box.TextChanged += (_, _) => ok.IsVisible = (box.Text ?? "").Trim() is { Length: > 0 } text && text != current;
			// Focus elsewhere: back to the title, unless it went to the tick (or a press on it is under way).
			box.LostFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				if (editor.IsVisible && !box.IsFocused && !ok.IsFocused && !ok.IsPointerOver && !saving)
				{
					Edit(false);
				}
			});
			ok.LostFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				if (editor.IsVisible && !box.IsFocused && !ok.IsFocused && !saving)
				{
					Edit(false);
				}
			});
			shown.Click += (_, _) => Edit(true);
			ok.Click += (_, _) => _ = CommitAsync();
			box.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					_ = CommitAsync();
				}
				else if (e.Key == Avalonia.Input.Key.Escape)
				{
					e.Handled = true;
					Edit(false);
				}
			};

			return new Panel { MinHeight = 40, Children = { shown, editor } };
		}

		/// <summary>
		/// About a member, as labelled lines: their username, status (online now, and the band they've
		/// joined), last login, and their bands with their role in each (and whether invited only).
		/// </summary>
		private static Control MemberDetails(SiteMember member, IReadOnlyList<Membership> memberships)
		{
			var status = new List<string>
			{
				memberships.Any(membership => membership.Member.Connected) ? L.T("Online now") : L.T("Offline"),
			};
			if (memberships.FirstOrDefault(membership => membership.Member.Joined) is { } joined)
			{
				status.Add(L.T("joined {0}", joined.Band.Name));
			}

			var bandLines = memberships.Count == 0
				? L.T("In no band")
				: string.Join("\n", memberships.Select(MembershipLine));
			var lines = new (string Label, string Value)[]
			{
				(L.T("Username"), "@" + member.UserName),
				(L.T("Status"), string.Join(" · ", status)),
				(L.T("Last login"), member.LastLogin is { } last ? last.ToLocalTime().ToString("g") : L.T("Never")),
				(L.T("Memberships"), bandLines),
			};

			return DetailsGrid(lines);
		}

		/// <summary>
		/// About a band, as labelled lines: its members (how many, and how many only invited), songs in
		/// its songbook, setlists, and sessions (with the last one's date).
		/// </summary>
		private Control BandDetails(GroupSummary band)
		{
			var bandPeople = bandMembers.GetValueOrDefault(band.Id) ?? [];
			var invited = bandPeople.Count(member => !member.Approved);
			var people = band.IsPrivate
				? L.T("Only you (private)")
				: invited > 0 ? L.T("{0} ({1} invited)", bandPeople.Count, invited) : bandPeople.Count.ToString();
			var content = bandContent.GetValueOrDefault(band.Id);
			string Count(int? value) => value?.ToString() ?? "–";
			var sessions = content is { LastSession: { } last }
				? L.T("{0} · last {1:d}", content.Sessions, last.ToLocalTime())
				: Count(content?.Sessions);
			return DetailsGrid(
			[
				(L.T("Members"), people),
				(L.T("Songs in songbook"), Count(content?.Songs)),
				(L.T("Setlists"), Count(content?.Setlists)),
				(L.T("Sessions"), sessions),
			]);
		}

		/// <summary>Labelled lines: the labels in a dimmed column, the values beside them.</summary>
		private static Control DetailsGrid(IReadOnlyList<(string Label, string Value)> lines)
		{
			var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 2, 0, 6) };
			for (var row = 0; row < lines.Count; row++)
			{
				grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				var label = new TextBlock { Text = lines[row].Label, Opacity = 0.6, Margin = new Thickness(0, 1, 16, 1) };
				var value = new TextBlock { Text = lines[row].Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1) };
				Grid.SetRow(label, row);
				Grid.SetRow(value, row);
				Grid.SetColumn(value, 1);
				grid.Children.Add(label);
				grid.Children.Add(value);
			}

			return grid;
		}

		/// <summary>A band and the member's place in it, e.g. "The Band · admin · invited".</summary>
		private static string MembershipLine(Membership membership)
		{
			if (membership.Band.IsPrivate)
			{
				return L.T("{0} (private)", membership.Band.Name);
			}

			var parts = new List<string> { membership.Band.Name, RoleName(membership.Member.Role) };
			if (!membership.Member.Approved)
			{
				parts.Add(L.T("invited"));
			}

			return string.Join(" · ", parts);
		}

		private static string RoleName(GroupRole role) => role switch
		{
			GroupRole.Admin => L.T("admin"),
			GroupRole.Conductor => L.T("conductor"),
			_ => L.T("member"),
		};

		private static Control Docked(Control control, Dock dock)
		{
			DockPanel.SetDock(control, dock);
			return control;
		}

		private static Control Card(Control content)
		{
			var card = new Border { Padding = new Thickness(12, 4), CornerRadius = new CornerRadius(8), Child = content };
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		private static TextBlock Message(string text, bool error)
		{
			var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
			if (error)
			{
				message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(LyriousTheme.ErrorBrush));
			}

			return message;
		}

		private static void Show(TextBlock message, string text, bool error)
		{
			message.Text = text;
			message.IsVisible = true;
			if (error)
			{
				message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(LyriousTheme.ErrorBrush));
			}
			else
			{
				message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(LyriousTheme.SuccessBrush));
			}
		}
	}
}
