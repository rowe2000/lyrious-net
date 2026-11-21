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
	/// <item>A band: open it (it becomes the current band, with its songbook, setlists and songs), its
	/// members (the band page, as its admin), and its band settings, here.</item>
	/// <item>A member: their name (changed here), username, bands and last login, and their own settings
	/// (e.g. the colours), changed here as JSON.</item>
	/// </list>
	/// Other members' private bands aren't shown: no one else sees those.
	/// </summary>
	public sealed class SiteManagerView : UserControl
	{
		private readonly ISetlistService service;
		private readonly Func<Guid, Task> openBand;
		private readonly Func<Guid, Task> showBandMembers;
		private readonly Func<Guid, ContentControl, Task> showBandSettings;
		private readonly StackPanel bands = new() { Spacing = 8 };
		private readonly StackPanel members = new() { Spacing = 8 };
		private readonly TextBlock bandsTitle = Section("");
		private readonly TextBlock membersTitle = Section("");
		private readonly TextBox filter = new() { PlaceholderText = L.T("Search bands and members"), MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
		private IReadOnlyList<GroupSummary> allBands = [];
		private IReadOnlyDictionary<Guid, IReadOnlyList<GroupMember>> bandMembers = new Dictionary<Guid, IReadOnlyList<GroupMember>>();
		private IReadOnlyList<SiteMember> allMembers = [];

		/// <param name="openBand">Makes the band the current one and shows its start page.</param>
		/// <param name="showBandMembers">Shows the band page with the band, its members and their roles.</param>
		/// <param name="showBandSettings">Puts the band's settings in the place given.</param>
		public SiteManagerView(ISetlistService service, Func<Guid, Task> openBand, Func<Guid, Task> showBandMembers, Func<Guid, ContentControl, Task> showBandSettings)
		{
			this.service = service;
			this.openBand = openBand;
			this.showBandMembers = showBandMembers;
			this.showBandSettings = showBandSettings;

			filter.TextChanged += (_, _) => Show();
			var form = new StackPanel
			{
				Spacing = 12,
				Margin = new Thickness(0, 0, 0, 16),
				Children =
				{
					new TextBlock
					{
						Text = L.T("As site manager you can see and change every band and member: open a band to work with its songbook, setlists and songs, change its members and settings, and change members' names and settings. Other members' private bands stay private."),
						TextWrapping = TextWrapping.Wrap,
						Opacity = 0.75,
					},
					filter,
					bandsTitle,
					bands,
					membersTitle,
					members,
				},
			};
			var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			Content = PageLayout.Page(L.T("Site manager"), scroller, closable: true, icon: AppIcons.SiteManager);
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

		/// <summary>A band: its name and members; open it, its members, and its settings (shown below it).</summary>
		private Control BandRow(GroupSummary band)
		{
			var count = bandMembers.GetValueOrDefault(band.Id)?.Count ?? 0;
			var details = band.IsPrivate ? L.T("Your private band") : count == 1 ? L.T("1 member") : L.T("{0} members", count);
			var open = new Button { Content = L.T("Open"), Classes = { "accent" } };
			open.Click += async (_, _) => await openBand(band.Id);
			var membersButton = new Button { Content = L.T("Members") };
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

			return Card(new StackPanel
			{
				Spacing = 6,
				Children =
				{
					new TextBlock { Text = band.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = details, Opacity = 0.7 },
					new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { open, membersButton, settingsButton } },
					settings,
				},
			});
		}

		/// <summary>A member: their name (changed here), username, bands and last login, and their own settings.</summary>
		private Control MemberRow(SiteMember member, IReadOnlyList<string> memberBands)
		{
			// The name in a box of its own height, with a tick beside it ("Save name" as its tip), shown
			// once the name differs from the saved one; the row as tall as the tick either way.
			var name = new TextBox { Text = member.Name, MinWidth = 200, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center };
			var saveName = HomeView.IconButton("M5,12 L10,17 L19,7", L.T("Save name"));
			saveName.VerticalAlignment = VerticalAlignment.Center;
			saveName.IsVisible = false;
			var message = Message("", error: false);
			message.IsVisible = false;
			name.TextChanged += (_, _) => saveName.IsVisible = !string.IsNullOrWhiteSpace(name.Text) && name.Text.Trim() != member.Name;
			saveName.Click += async (_, _) =>
			{
				try
				{
					await service.RenameMemberAsync(member.Id, name.Text!.Trim());
					member = member with { Name = name.Text!.Trim() };
					saveName.IsVisible = false;
					Show(message, L.T("The name has been changed."), error: false);
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't change the name. {0}", ex.Message), error: true);
				}
			};

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
							new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, MinHeight = 40, Children = { name, saveName } },
							settingsButton,
						},
					},
					new TextBlock { Text = string.Join(" · ", info), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
					message,
					settingsPanel,
				},
			});
		}

		private static Control Card(Control content)
		{
			var card = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Child = content };
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		private static TextBlock Section(string title) => new() { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) };

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
