using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Login;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The signed-in app: the current page with two drawers, opened from buttons in the top corners.
	/// The left drawer holds the member, buttons showing the current band, setlist and song (each opens a
	/// list on the page to choose another) and account actions; the right drawer lists the
	/// current setlist's songs with key, tempo and beat, for jumping to another song. Each drawer can be
	/// pinned: it then docks beside the page and stays open, instead of sliding over it and closing
	/// after use. Opens on the page chosen by <see cref="StartPage.DetermineAsync"/> and saves the
	/// selected setlist and song to the group, so the next login resumes there.
	/// </summary>
	public sealed class HomeView : UserControl
	{
		private const string MenuIcon = "M4,7 H20 M4,12 H20 M4,17 H20";
		// A list icon: three lines with a dot in front of each.
		private const string ListIcon = "M9,7 H20 M9,12 H20 M9,17 H20 M4.5,7 H5 M4.5,12 H5 M4.5,17 H5";
		private const string NoSong = "Choose a song";
		private const string AddIcon = "M12,5 V19 M5,12 H19";
		private const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";
		// A pushpin: head, body widening to a base, and the needle.
		private const string PinIcon = "M9,3 H15 M10,3 V9 L7,13 H17 L14,9 V3 M12,13 V21";

		private readonly ISetlistService service;
		private readonly TextBlock groupName = new() { Text = "Choose a band", FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly TextBlock setlistTitle = new() { Text = "Setlist", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
		private readonly TextBlock setlistSubtitle = new() { Opacity = 0.7, IsVisible = false };
		private readonly ContentControl setlistDrawerBody = new();
		private readonly TextBlock currentSetlistName = new() { Text = "Choose a setlist", FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly TextBlock currentSongName = new() { Text = NoSong, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly Button chooseSetlistButton;
		private readonly Button chooseSongButton;
		private readonly Button chooseGroupButton;
		private readonly Button addSongsButton = IconButton(AddIcon, "Add songs from the songbook");
		private readonly ContentControl page = new();
		private readonly SplitView menuDrawer;
		private readonly SplitView setlistDrawer;

		// What the app is showing, so the setlist drawer knows which setlist and song are current.
		private GroupSummary? currentGroup;
		private SetlistSummary? currentSetlist;
		private Guid? currentEntryId;
		private Guid? currentSongId;

		/// <summary>Raised when the user wants to change their password.</summary>
		public event Action? ChangePasswordRequested;

		/// <summary>Raised when the user wants to log out.</summary>
		public event Action? LogOutRequested;

		/// <summary>Raised when the user wants the password saved on this device removed.</summary>
		public event Action? ForgetSavedPasswordRequested;

		/// <param name="offerForgetSavedPassword">Shows "Forget saved password" in the menu (when a password is saved and the app can remove it).</param>
		public HomeView(AuthenticatedUser user, ISetlistService service, bool offerForgetSavedPassword = false)
		{
			this.service = service;
			chooseSetlistButton = DrawerPicker("Setlist", currentSetlistName, "Choose a setlist", () =>
			{
				if (currentGroup is { } group)
				{
					_ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
				}
			});
			chooseSetlistButton.IsEnabled = false;
			chooseSongButton = DrawerPicker("Song", currentSongName, "Choose a song", () =>
			{
				if (currentGroup is { } group)
				{
					_ = RunAsync(async () => ShowSongs(group, await service.GetSongbookSongsAsync(group.Id)));
				}
			});
			chooseSongButton.IsEnabled = false;
			chooseGroupButton = DrawerPicker("Band", groupName, "Choose a band", () => _ = RunAsync(ShowGroupsAsync));
			chooseGroupButton.Margin = new Thickness(0, 12, 0, 0);

			var menuButton = IconButton(MenuIcon, "Open menu");
			menuButton.HorizontalAlignment = HorizontalAlignment.Left;

			var setlistButton = IconButton(ListIcon, "Open setlist");
			setlistButton.HorizontalAlignment = HorizontalAlignment.Right;

			var topRow = new Grid { Margin = new Thickness(8), Children = { menuButton, setlistButton } };
			DockPanel.SetDock(topRow, Dock.Top);

			setlistDrawer = Drawer(SplitViewPanePlacement.Right, 320);
			setlistDrawer.Content = new DockPanel { Children = { topRow, page } };
			setlistDrawer.Pane = SetlistDrawerContent(setlistButton);

			menuDrawer = Drawer(SplitViewPanePlacement.Left, 280);
			menuDrawer.Content = setlistDrawer;
			menuDrawer.Pane = MenuDrawerContent(user, menuButton, offerForgetSavedPassword);

			menuButton.Click += (_, _) => menuDrawer.IsPaneOpen = true;
			setlistButton.Click += (_, _) =>
			{
				setlistDrawer.IsPaneOpen = true;
				_ = LoadSetlistDrawerAsync();
			};

			Content = menuDrawer;

			_ = StartAsync();
		}

		/// <summary>A drawer, unpinned: it slides over the page and closes when tapping outside it.</summary>
		private static SplitView Drawer(SplitViewPanePlacement placement, double width)
		{
			var drawer = new SplitView
			{
				DisplayMode = SplitViewDisplayMode.Overlay,
				PanePlacement = placement,
				OpenPaneLength = width,
				IsPaneOpen = false,
			};
			drawer.Bind(SplitView.PaneBackgroundProperty, drawer.GetResourceObservable(LyriousTheme.CardBrush));
			return drawer;
		}

		private static bool IsPinned(SplitView drawer) => drawer.DisplayMode == SplitViewDisplayMode.Inline;

		/// <summary>
		/// The ✕ and pin buttons for a drawer's header. Pinning docks the drawer beside the page
		/// (it stays open and the page makes room); unpinning makes it slide over the page again.
		/// ✕ hides the drawer but keeps it pinned, so it docks again when reopened. While a pinned
		/// drawer is showing, its open button in the top corner is hidden.
		/// </summary>
		private static StackPanel DrawerButtons(SplitView drawer, Button openButton, string name)
		{
			var pinButton = IconButton(PinIcon, $"Pin {name}");
			var closeButton = IconButton(CloseIcon, $"Close {name}");
			closeButton.Click += (_, _) => drawer.IsPaneOpen = false;

			void Update()
			{
				var pinned = IsPinned(drawer);
				// Upright and highlighted when pinned, tilted when not.
				((Path)pinButton.Content!).RenderTransform = pinned ? null : new RotateTransform(45);
				if (pinned)
				{
					pinButton.Classes.Add("accent");
				}
				else
				{
					pinButton.Classes.Remove("accent");
				}

				var description = pinned ? $"Unpin {name}" : $"Pin {name}";
				ToolTip.SetTip(pinButton, description);
				Avalonia.Automation.AutomationProperties.SetName(pinButton, description);
				openButton.IsVisible = !(pinned && drawer.IsPaneOpen);
			}

			pinButton.Click += (_, _) =>
			{
				drawer.DisplayMode = IsPinned(drawer) ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
				Update();
			};
			drawer.PaneOpened += (_, _) => Update();
			drawer.PaneClosed += (_, _) => Update();
			Update();

			return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { pinButton, closeButton } };
		}

		/// <summary>Closes the drawer after an action, unless it's pinned.</summary>
		private static void CloseUnlessPinned(SplitView drawer)
		{
			if (!IsPinned(drawer))
			{
				drawer.IsPaneOpen = false;
			}
		}

		/// <summary>
		/// A button at the top of the left drawer that shows what is current (the band, the setlist)
		/// under a small caption, and opens a list on the page to choose another.
		/// </summary>
		private Button DrawerPicker(string caption, TextBlock current, string description, Action choose)
		{
			var button = new Button
			{
				Content = new StackPanel
				{
					Spacing = 2,
					Children = { new TextBlock { Text = caption, FontSize = 13, Opacity = 0.75 }, current },
				},
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(12, 8),
			};
			ToolTip.SetTip(button, description);
			button.Click += (_, _) =>
			{
				CloseUnlessPinned(menuDrawer);
				choose();
			};
			return button;
		}

		/// <summary>Records the setlist the app is showing, and shows its name on the left drawer's button.</summary>
		private void SetCurrentSetlist(SetlistSummary setlist)
		{
			currentSetlist = setlist;
			currentSetlistName.Text = setlist.Name;
			addSongsButton.IsEnabled = true;
		}

		/// <summary>Records the song the app is showing (and its setlist entry, if it's shown from a setlist), for the Song button and the highlights.</summary>
		private void SetCurrentSong(SongDetails? song, Guid? entryId)
		{
			currentSongId = song?.Id;
			currentEntryId = entryId;
			currentSongName.Text = song?.Name ?? NoSong;
		}

		/// <summary>The left drawer: member, band, setlist and song at the top, account actions at the bottom.</summary>
		private Control MenuDrawerContent(AuthenticatedUser user, Button openButton, bool offerForgetSavedPassword)
		{
			var buttons = DrawerButtons(menuDrawer, openButton, "menu");
			DockPanel.SetDock(buttons, Dock.Right);

			var changePassword = DrawerItem("Change password");
			changePassword.Click += (_, _) =>
			{
				CloseUnlessPinned(menuDrawer);
				ChangePasswordRequested?.Invoke();
			};

			var logOut = DrawerItem("Log out");
			logOut.Click += (_, _) =>
			{
				menuDrawer.IsPaneOpen = false;
				LogOutRequested?.Invoke();
			};

			var header = new StackPanel
			{
				Spacing = 4,
				Children =
				{
					new DockPanel { Children = { buttons, new TextBlock { Text = user.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } } },
					chooseGroupButton,
					chooseSetlistButton,
					chooseSongButton,
				},
			};
			DockPanel.SetDock(header, Dock.Top);

			// The account actions sit at the bottom of the drawer.
			var account = new StackPanel { Spacing = 4, Children = { changePassword } };
			DockPanel.SetDock(account, Dock.Bottom);

			if (offerForgetSavedPassword)
			{
				var forget = DrawerItem("Forget saved password");
				forget.Click += (_, _) =>
				{
					// Gone once used: there's nothing saved any more.
					account.Children.Remove(forget);
					ForgetSavedPasswordRequested?.Invoke();
				};
				account.Children.Add(forget);
			}

			account.Children.Add(logOut);

			return new DockPanel
			{
				Margin = new Thickness(16, 8, 8, 16),
				LastChildFill = false,
				Children = { header, account },
			};
		}

		/// <summary>The right drawer: a fixed header (setlist name, song count, "+", pin and ✕) and the song list.</summary>
		private Control SetlistDrawerContent(Button openButton)
		{
			var buttons = DrawerButtons(setlistDrawer, openButton, "setlist");
			DockPanel.SetDock(buttons, Dock.Right);

			// "+" in front of pin and ✕: add songs from the songbook, as on the setlist page.
			addSongsButton.IsEnabled = false;
			addSongsButton.Click += (_, _) =>
			{
				if (currentGroup is not { } group || currentSetlist is not { } setlist)
				{
					return;
				}

				CloseUnlessPinned(setlistDrawer);
				_ = RunAsync(async () =>
				{
					var entries = await service.GetEntriesAsync(setlist.Id);
					ShowAddSongs(group, setlist, await service.GetSongbookSongsAsync(group.Id), entries.Select(entry => entry.Song.Id).ToHashSet());
				});
			};
			buttons.Children.Insert(0, addSongsButton);

			var header = new StackPanel
			{
				Spacing = 2,
				Margin = new Thickness(0, 0, 0, 12),
				Children = { new DockPanel { Children = { buttons, setlistTitle } }, setlistSubtitle },
			};
			DockPanel.SetDock(header, Dock.Top);

			return new DockPanel { Margin = new Thickness(16, 8, 8, 16), Children = { header, setlistDrawerBody } };
		}

		/// <summary>Fills the right drawer with the current setlist's songs, loaded fresh each time.</summary>
		private async Task LoadSetlistDrawerAsync()
		{
			if (currentGroup is not { } group || currentSetlist is not { } setlist)
			{
				SetSetlistHeader("Setlist", null);
				setlistDrawerBody.Content = DrawerMessage("No setlist is selected yet. Choose one from the list of setlists.");
				return;
			}

			SetSetlistHeader(setlist.Name, null);
			setlistDrawerBody.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			try
			{
				var entries = await service.GetEntriesAsync(setlist.Id);
				SetSetlistHeader(setlist.Name, entries.Count == 1 ? "1 song" : $"{entries.Count} songs");

				var list = new StackPanel { Spacing = 6 };
				list.Children.AddRange(entries.Select((entry, index) => (Control)SongRow(group, setlist, entry, index)));
				setlistDrawerBody.Content = entries.Count == 0
					? DrawerMessage("There are no songs in this setlist yet.")
					: new ScrollViewer { Content = list };
			}
			catch (Exception ex)
			{
				setlistDrawerBody.Content = DrawerMessage($"Couldn't load the songs. {ex.Message}");
			}
		}

		private void SetSetlistHeader(string title, string? subtitle)
		{
			setlistTitle.Text = title;
			setlistSubtitle.Text = subtitle;
			setlistSubtitle.IsVisible = subtitle != null;
		}

		/// <summary>Keeps an open setlist drawer in step with the page (e.g. the highlighted song).</summary>
		private void RefreshSetlistDrawer()
		{
			if (setlistDrawer.IsPaneOpen)
			{
				_ = LoadSetlistDrawerAsync();
			}
		}

		/// <summary>One song in the right drawer: position and name, then key, tempo and beat; the current song is highlighted.</summary>
		private Button SongRow(GroupSummary group, SetlistSummary setlist, SetlistEntry entry, int index)
		{
			var details = PageLayout.SongInfo(entry.Song);
			var content = new StackPanel { Spacing = 2 };
			content.Children.Add(new TextBlock { Text = $"{index + 1}. {entry.Song.Name}", FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
			if (details.Length > 0)
			{
				content.Children.Add(new TextBlock { Text = details, FontSize = 13, Opacity = 0.75 });
			}

			var row = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(12, 8),
			};
			if (entry.Id == currentEntryId)
			{
				row.Classes.Add("accent");
			}

			row.Click += (_, _) =>
			{
				CloseUnlessPinned(setlistDrawer);
				_ = RunAsync(async () =>
				{
					await service.SelectSongAsync(group.Id, entry);
					ShowLyrics(group, setlist, entry);
				});
			};
			return row;
		}

		private static TextBlock DrawerMessage(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

		private static Button DrawerItem(string text)
		{
			return new Button
			{
				Content = text,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(12, 10),
			};
		}

		/// <summary>
		/// A button showing a stroked icon. Icons are drawn as shapes because the browser has no system
		/// fonts to supply symbol glyphs such as ☰ or ✕.
		/// </summary>
		internal static Button IconButton(string pathData, string description)
		{
			var icon = new Path
			{
				Data = Geometry.Parse(pathData),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 24,
				Height = 24,
				RenderTransformOrigin = RelativePoint.Center,
			};
			icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable(LyriousTheme.ForegroundBrush));

			var button = new Button { Content = icon, Padding = new Thickness(8), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
			ToolTip.SetTip(button, description);
			Avalonia.Automation.AutomationProperties.SetName(button, description);
			return button;
		}

		private Task StartAsync() => RunAsync(OpenStartPageAsync);

		/// <summary>Opens the start page of the member's current group (after login, and after choosing another band).</summary>
		private async Task OpenStartPageAsync()
		{
			var start = await StartPage.DetermineAsync(service);
			groupName.Text = start.Group?.Name ?? "Choose a band";
			currentGroup = start.Group;
			currentSetlist = null;
			SetCurrentSong(null, null);
			currentSetlistName.Text = "Choose a setlist";
			addSongsButton.IsEnabled = false;
			chooseSetlistButton.IsEnabled = start.Group != null;
			chooseSongButton.IsEnabled = start.Group != null;

			switch (start.Kind)
			{
				case StartPageKind.Lyrics:
					ShowLyrics(start.Group!, start.Setlist!, start.Entry!);
					break;
				case StartPageKind.Setlist:
					await ShowSetlistAsync(start.Group!, start.Setlist!);
					break;
				case StartPageKind.Setlists:
					ShowSetlists(start.Group!, start.Setlists!);
					break;
				default:
					ShowMessage("You're not a member of a band yet. Ask a band admin to invite you.");
					break;
			}

			RefreshSetlistDrawer();
		}

		/// <summary>The member's bands with their members, to choose the one to work with.</summary>
		private async Task ShowGroupsAsync()
		{
			var groups = await service.GetGroupsAsync();
			var members = await service.GetGroupMembersAsync();
			var view = new GroupsView(groups, members, currentGroup?.Id);
			view.GroupSelected += group => _ = RunAsync(async () =>
			{
				await service.SelectGroupAsync(group.Id);
				await OpenStartPageAsync();
			});
			// A new band becomes the current one straight away.
			view.GroupCreateRequested += name => _ = RunAsync(async () =>
			{
				var group = await service.CreateGroupAsync(name);
				await service.SelectGroupAsync(group.Id);
				await OpenStartPageAsync();
			});
			page.Content = view;
		}

		private void ShowSetlists(GroupSummary group, IReadOnlyList<SetlistSummary> setlists)
		{
			var view = new SetlistsView(group, setlists);
			view.SetlistSelected += setlist => _ = RunAsync(async () =>
			{
				await service.SelectSetlistAsync(group.Id, setlist.Id);
				SetCurrentSong(null, null);
				await ShowSetlistAsync(group, setlist);
			});
			view.SetlistCreateRequested += name => _ = RunAsync(async () =>
			{
				await service.CreateSetlistAsync(group.Id, name);
				ShowSetlists(group, await service.GetSetlistsAsync(group.Id));
			});
			page.Content = view;
		}

		private async Task ShowSetlistAsync(GroupSummary group, SetlistSummary setlist)
		{
			var entries = await service.GetEntriesAsync(setlist.Id);
			SetCurrentSetlist(setlist);

			var view = new SetlistView(setlist, entries);
			view.SongSelected += entry => _ = RunAsync(async () =>
			{
				await service.SelectSongAsync(group.Id, entry);
				ShowLyrics(group, setlist, entry);
			});
			view.AllSetlistsRequested += () => _ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
			view.AddSongsRequested += () => _ = RunAsync(async () =>
				ShowAddSongs(group, setlist, await service.GetSongbookSongsAsync(group.Id), entries.Select(entry => entry.Song.Id).ToHashSet()));
			page.Content = view;
			RefreshSetlistDrawer();
		}

		/// <summary>The songbook, to pick songs to add to the setlist; back to the setlist afterwards.</summary>
		private void ShowAddSongs(GroupSummary group, SetlistSummary setlist, IReadOnlyList<SongDetails> songbook, IReadOnlySet<Guid> songsInSetlist)
		{
			var view = new AddSongsView(setlist, songbook, songsInSetlist);
			view.SongsAdded += songs => _ = RunAsync(async () =>
			{
				await service.AddSongsAsync(setlist.Id, songs.Select(song => song.Id).ToArray());
				await ShowSetlistAsync(group, setlist);
			});
			view.BackRequested += () => _ = RunAsync(() => ShowSetlistAsync(group, setlist));
			page.Content = view;
		}

		private void ShowLyrics(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			SetCurrentSetlist(setlist);
			SetCurrentSong(entry.Song, entry.Id);

			var view = new LyricScrollView(entry.Song, $"‹ {setlist.Name}", lyrics => service.UpdateLyricsAsync(entry.Song.Id, lyrics));
			view.BackRequested += () => _ = RunAsync(() => ShowSetlistAsync(group, setlist));
			page.Content = view;
			RefreshSetlistDrawer();
		}

		/// <summary>
		/// The band's songbook, to choose a song. A song that is in the current setlist opens from
		/// there (and becomes the group's current song); any other song just shows its lyrics.
		/// </summary>
		private void ShowSongs(GroupSummary group, IReadOnlyList<SongDetails> songs)
		{
			var view = new SongsView(group, songs, currentSongId);
			view.SongSelected += song => _ = RunAsync(async () =>
			{
				if (currentSetlist is { } setlist
				    && (await service.GetEntriesAsync(setlist.Id)).FirstOrDefault(entry => entry.Song.Id == song.Id) is { } entry)
				{
					await service.SelectSongAsync(group.Id, entry);
					ShowLyrics(group, setlist, entry);
				}
				else
				{
					ShowSongbookLyrics(group, song);
				}
			});
			page.Content = view;
		}

		/// <summary>The lyrics of a song that isn't in the current setlist; back goes to the songbook.</summary>
		private void ShowSongbookLyrics(GroupSummary group, SongDetails song)
		{
			SetCurrentSong(song, null);

			var view = new LyricScrollView(song, "‹ Songbook", lyrics => service.UpdateLyricsAsync(song.Id, lyrics));
			view.BackRequested += () => _ = RunAsync(async () => ShowSongs(group, await service.GetSongbookSongsAsync(group.Id)));
			page.Content = view;
			RefreshSetlistDrawer();
		}

		/// <summary>Shows a loading indicator while the action runs, and the error (with a retry) if it fails.</summary>
		private async Task RunAsync(Func<Task> action)
		{
			var previous = page.Content;
			page.Content = new ProgressBar { IsIndeterminate = true, Width = 200, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			try
			{
				await action();
			}
			catch (Exception ex)
			{
				var retry = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Center };
				retry.Click += (_, _) => _ = previous == null ? StartAsync() : RunAsync(action);
				ShowMessage($"Something went wrong. {ex.Message}", retry);
			}
		}

		private void ShowMessage(string message, Control? action = null)
		{
			var panel = new StackPanel { Spacing = 16, MaxWidth = 480, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
			if (action != null)
			{
				panel.Children.Add(action);
			}

			page.Content = panel;
		}
	}
}
