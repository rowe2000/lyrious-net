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
	/// The left drawer holds the group, the member and account actions; the right drawer lists the
	/// current setlist's songs with key and tempo, for jumping to another song. Each drawer can be
	/// pinned: it then docks beside the page and stays open, instead of sliding over it and closing
	/// after use. Opens on the page chosen by <see cref="StartPage.DetermineAsync"/> and saves the
	/// selected setlist and song to the group, so the next login resumes there.
	/// </summary>
	public sealed class HomeView : UserControl
	{
		private const string MenuIcon = "M4,7 H20 M4,12 H20 M4,17 H20";
		// A list icon: three lines with a dot in front of each.
		private const string ListIcon = "M9,7 H20 M9,12 H20 M9,17 H20 M4.5,7 H5 M4.5,12 H5 M4.5,17 H5";
		private const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";
		// A pushpin: head, body widening to a base, and the needle.
		private const string PinIcon = "M9,3 H15 M10,3 V9 L7,13 H17 L14,9 V3 M12,13 V21";

		private readonly ISetlistService service;
		private readonly TextBlock groupName = new() { Text = "Lyrious", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
		private readonly TextBlock setlistTitle = new() { Text = "Setlist", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
		private readonly TextBlock setlistSubtitle = new() { Opacity = 0.7, IsVisible = false };
		private readonly ContentControl setlistDrawerBody = new();
		private readonly ContentControl page = new();
		private readonly SplitView menuDrawer;
		private readonly SplitView setlistDrawer;

		// What the app is showing, so the setlist drawer knows which setlist and song are current.
		private GroupSummary? currentGroup;
		private SetlistSummary? currentSetlist;
		private Guid? currentEntryId;

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

		/// <summary>The left drawer: group and member, then account actions.</summary>
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

			var panel = new StackPanel
			{
				Margin = new Thickness(16, 8, 8, 16),
				Spacing = 4,
				Children =
				{
					new DockPanel { Children = { buttons, groupName } },
					new TextBlock { Text = user.DisplayName, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
					changePassword,
				},
			};

			if (offerForgetSavedPassword)
			{
				var forget = DrawerItem("Forget saved password");
				forget.Click += (_, _) =>
				{
					// Gone once used: there's nothing saved any more.
					panel.Children.Remove(forget);
					ForgetSavedPasswordRequested?.Invoke();
				};
				panel.Children.Add(forget);
			}

			panel.Children.Add(logOut);
			return panel;
		}

		/// <summary>The right drawer: a fixed header (setlist name, song count, pin and ✕) and the song list.</summary>
		private Control SetlistDrawerContent(Button openButton)
		{
			var buttons = DrawerButtons(setlistDrawer, openButton, "setlist");
			DockPanel.SetDock(buttons, Dock.Right);

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

		/// <summary>One song in the right drawer: position and name, then key and tempo; the current song is highlighted.</summary>
		private Button SongRow(GroupSummary group, SetlistSummary setlist, SetlistEntry entry, int index)
		{
			var details = PageLayout.SongInfo(entry.Song, includeBeat: false);
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
		private static Button IconButton(string pathData, string description)
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

		private Task StartAsync() => RunAsync(async () =>
		{
			var start = await StartPage.DetermineAsync(service);
			groupName.Text = start.Group?.Name ?? "Lyrious";
			currentGroup = start.Group;

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
					ShowMessage("You're not a member of a group yet. Ask a group admin to invite you.");
					break;
			}
		});

		private void ShowSetlists(GroupSummary group, IReadOnlyList<SetlistSummary> setlists)
		{
			var view = new SetlistsView(group, setlists);
			view.SetlistSelected += setlist => _ = RunAsync(async () =>
			{
				await service.SelectSetlistAsync(group.Id, setlist.Id);
				currentEntryId = null;
				await ShowSetlistAsync(group, setlist);
			});
			page.Content = view;
		}

		private async Task ShowSetlistAsync(GroupSummary group, SetlistSummary setlist)
		{
			var entries = await service.GetEntriesAsync(setlist.Id);
			currentSetlist = setlist;

			var view = new SetlistView(setlist, entries);
			view.SongSelected += entry => _ = RunAsync(async () =>
			{
				await service.SelectSongAsync(group.Id, entry);
				ShowLyrics(group, setlist, entry);
			});
			view.AllSetlistsRequested += () => _ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
			page.Content = view;
			RefreshSetlistDrawer();
		}

		private void ShowLyrics(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			currentSetlist = setlist;
			currentEntryId = entry.Id;

			var view = new LyricScrollView(setlist, entry);
			view.BackRequested += () => _ = RunAsync(() => ShowSetlistAsync(group, setlist));
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
