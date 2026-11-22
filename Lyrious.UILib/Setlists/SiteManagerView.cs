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
		private readonly Func<Guid, Task> showBandMembers;
		private readonly Func<Guid, ContentControl, Task> showBandSettings;
		private readonly StackPanel bands = new() { Spacing = 8 };
		private readonly StackPanel members = new() { Spacing = 8 };
		// The tabs' headers, with the number of bands and members the search shows.
		private readonly TextBlock bandsTitle = new() { FontSize = 16 };
		private readonly TextBlock membersTitle = new() { FontSize = 16 };
		private readonly TextBox filter = new() { PlaceholderText = L.T("Search bands and members"), MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
		private IReadOnlyList<GroupSummary> allBands = [];
		private IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> bandMembers = new Dictionary<Guid, IReadOnlyList<GroupMember>>();
		private IReadOnlyList<SiteMember> allMembers = [];

		/// <param name="showBandMembers">Shows the band page with the band, its members and their roles.</param>
		/// <param name="showBandSettings">Puts the band's settings in the place given.</param>
		public SiteManagerView(ISetlistService service, Func<Guid, Task> showBandMembers, Func<Guid, ContentControl, Task> showBandSettings)
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
				Margin = new Thickness(0, 8, 0, 0),
				ItemsSource = new[]
				{
					new TabItem { Header = bandsTitle, Content = Scrolling(bands) },
					new TabItem { Header = membersTitle, Content = Scrolling(members) },
				},
			};
			Content = PageLayout.Page(L.T("Site manager"), new DockPanel { Children = { top, tabs } }, closable: true, icon: AppIcons.SiteManager);
			_ = LoadAsync();
		}

		private async Task LoadAsync()
		{
			try
			{
				allBands = (await service.GetGroupsAsync()).OrderBy(band => band.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
				bandMembers = await service.GetGroupMembersAsync();
				allMembers = await service.GetAllMembersAsync();
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

			var bandNames = allBands.ToDictionary(band => band.Id, band => band.Name);
			var bandsOf = bandMembers
				.SelectMany(entry => entry.Value.Select(member => (member.MemberId, Band: bandNames.GetValueOrDefault(entry.Key))))
				.Where(entry => entry.Band != null)
				.GroupBy(entry => entry.MemberId)
				.ToDictionary(group => group.Key, group => group.Select(entry => entry.Band!).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList());

			var shownBands = allBands.Where(band => Matches(band.Name)).ToList();
			bandsTitle.Text = L.T("Bands ({0})", shownBands.Count);
			bands.Children.Clear();
			foreach (var band in shownBands)
			{
				bands.Children.Add(BandRow(band));
			}

			var shownMembers = allMembers
				.Where(member => Matches(member.Name) || Matches(member.UserName) || (bandsOf.GetValueOrDefault(member.Id)?.Any(Matches) ?? false))
				.ToList();
			membersTitle.Text = L.T("Members ({0})", shownMembers.Count);
			members.Children.Clear();
			foreach (var member in shownMembers)
			{
				members.Children.Add(MemberRow(member, bandsOf.GetValueOrDefault(member.Id) ?? []));
			}
		}

		/// <summary>A band: its name (click to change) and members; its members page, and its settings (shown below it).</summary>
		private Control BandRow(GroupSummary band)
		{
			var count = bandMembers.GetValueOrDefault(band.Id)?.Count ?? 0;
			var details = band.IsPrivate ? L.T("Your private band") : count == 1 ? L.T("1 member") : L.T("{0} members", count);
			var membersButton = new Button { Content = L.T("Members"), IsVisible = !band.IsPrivate };
			membersButton.Click += async (_, _) => await showBandMembers(band.Id);
			var settingsButton = new Button { Content = L.T("Band settings") };
			var settings = new ContentControl { IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
			var loaded = false;
			settingsButton.Click += async (_, _) =>
			{
				settings.IsVisible = !settings.IsVisible;
				if (settings.IsVisible && !loaded)
				{
					loaded = true;
					await showBandSettings(band.Id, settings);
				}
			};

			var message = Message("", error: false);
			message.IsVisible = false;
			// The private band keeps its name (in italics, not to be changed) and has no members to manage.
			Control name = band.IsPrivate
				? new TextBlock { Text = band.Name, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, MinHeight = 40, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 10) }
				: EditableName(band.Name, L.T("Rename band"), message, async newName =>
				{
					await service.RenameGroupAsync(band.Id, newName);
					band = band with { Name = newName };
					allBands = allBands.Select(other => other.Id == band.Id ? band : other).ToList();
				});

			return Card(new StackPanel
			{
				Spacing = 6,
				Children =
				{
					name,
					new TextBlock { Text = details, Opacity = 0.7 },
					message,
					new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { membersButton, settingsButton } },
					settings,
				},
			});
		}

		/// <summary>A member: their name (changed here), username, bands and last login, and their own settings.</summary>
		private Control MemberRow(SiteMember member, IReadOnlyList<string> memberBands)
		{
			var message = Message("", error: false);
			message.IsVisible = false;
			var name = EditableName(member.Name, L.T("Change name"), message, async newName =>
			{
				await service.RenameMemberAsync(member.Id, newName);
				member = member with { Name = newName };
			});

			var info = new List<string> { "@" + member.UserName };
			info.Add(memberBands.Count > 0 ? string.Join(", ", memberBands) : L.T("In no band"));
			info.Add(member.LastLogin is { } last ? L.T("Last login {0:g}", last.ToLocalTime()) : L.T("Never logged in"));

			// Their own settings (the colours and the like), as JSON: read when opened, saved with Save.
			var settingsButton = new Button { Content = L.T("Settings"), VerticalAlignment = VerticalAlignment.Center };
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
			settingsButton.Click += async (_, _) =>
			{
				settingsPanel.IsVisible = !settingsPanel.IsVisible;
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
				Spacing = 6,
				Children =
				{
					new WrapPanel
					{
						Orientation = Orientation.Horizontal,
						ItemSpacing = 8,
						LineSpacing = 8,
						Children =
						{
							name,
							settingsButton,
						},
					},
					new TextBlock { Text = string.Join(" · ", info), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
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

		private static Control Card(Control content)
		{
			var card = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Child = content };
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
