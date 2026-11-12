using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Login;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The signed-in app: the current page, with a closable drawer on the left (group, member and
	/// account actions) opened from a menu button. Opens on the page chosen by
	/// <see cref="StartPage.DetermineAsync"/> and saves the selected setlist and song to the group,
	/// so the next login resumes there.
	/// </summary>
	public sealed class HomeView : UserControl
	{
		private readonly ISetlistService service;
		private readonly TextBlock groupName = new() { Text = "Lyrious", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
		private readonly ContentControl page = new();
		private readonly SplitView drawer;

		/// <summary>Raised when the user wants to change their password.</summary>
		public event Action? ChangePasswordRequested;

		/// <summary>Raised when the user wants to log out.</summary>
		public event Action? LogOutRequested;

		public HomeView(AuthenticatedUser user, ISetlistService service)
		{
			this.service = service;

			var menuButton = IconButton("M4,7 H20 M4,12 H20 M4,17 H20", "Open menu");
			menuButton.Margin = new Thickness(8);
			menuButton.HorizontalAlignment = HorizontalAlignment.Left;
			menuButton.Click += (_, _) => drawer!.IsPaneOpen = true;
			DockPanel.SetDock(menuButton, Dock.Top);

			drawer = new SplitView
			{
				// Overlay: the drawer slides over the page and closes when tapping outside it.
				DisplayMode = SplitViewDisplayMode.Overlay,
				PanePlacement = SplitViewPanePlacement.Left,
				OpenPaneLength = 280,
				IsPaneOpen = false,
				Pane = DrawerContent(user),
				Content = new DockPanel { Children = { menuButton, page } },
			};
			drawer.Bind(SplitView.PaneBackgroundProperty, drawer.GetResourceObservable(LyriousTheme.CardBrush));

			Content = drawer;

			_ = StartAsync();
		}

		/// <summary>The drawer: group and member, then account actions; closes from its ✕ button or after an action.</summary>
		private Control DrawerContent(AuthenticatedUser user)
		{
			var closeButton = IconButton("M6,6 L18,18 M18,6 L6,18", "Close menu");
			closeButton.Click += (_, _) => drawer.IsPaneOpen = false;
			DockPanel.SetDock(closeButton, Dock.Right);

			var changePassword = DrawerItem("Change password");
			changePassword.Click += (_, _) =>
			{
				drawer.IsPaneOpen = false;
				ChangePasswordRequested?.Invoke();
			};

			var logOut = DrawerItem("Log out");
			logOut.Click += (_, _) =>
			{
				drawer.IsPaneOpen = false;
				LogOutRequested?.Invoke();
			};

			return new StackPanel
			{
				Margin = new Thickness(16, 8, 8, 16),
				Spacing = 4,
				Children =
				{
					new DockPanel { Children = { closeButton, groupName } },
					new TextBlock { Text = user.DisplayName, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
					changePassword,
					logOut,
				},
			};
		}

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
			var icon = new Avalonia.Controls.Shapes.Path
			{
				Data = Geometry.Parse(pathData),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				Width = 24,
				Height = 24,
			};
			icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, icon.GetResourceObservable(LyriousTheme.ForegroundBrush));

			var button = new Button { Content = icon, Padding = new Thickness(8), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
			ToolTip.SetTip(button, description);
			Avalonia.Automation.AutomationProperties.SetName(button, description);
			return button;
		}

		private Task StartAsync() => RunAsync(async () =>
		{
			var start = await StartPage.DetermineAsync(service);
			groupName.Text = start.Group?.Name ?? "Lyrious";

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

		private void ShowSetlists(GroupSummary group, System.Collections.Generic.IReadOnlyList<SetlistSummary> setlists)
		{
			var view = new SetlistsView(group, setlists);
			view.SetlistSelected += setlist => _ = RunAsync(async () =>
			{
				await service.SelectSetlistAsync(group.Id, setlist.Id);
				await ShowSetlistAsync(group, setlist);
			});
			page.Content = view;
		}

		private async Task ShowSetlistAsync(GroupSummary group, SetlistSummary setlist)
		{
			var entries = await service.GetEntriesAsync(setlist.Id);
			var view = new SetlistView(setlist, entries);
			view.SongSelected += entry => _ = RunAsync(async () =>
			{
				await service.SelectSongAsync(group.Id, entry);
				ShowLyrics(group, setlist, entry);
			});
			view.AllSetlistsRequested += () => _ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
			page.Content = view;
		}

		private void ShowLyrics(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			var view = new LyricScrollView(setlist, entry);
			view.BackRequested += () => _ = RunAsync(() => ShowSetlistAsync(group, setlist));
			page.Content = view;
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
