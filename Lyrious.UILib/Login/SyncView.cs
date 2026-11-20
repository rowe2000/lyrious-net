using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Setlists;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// "Sync": whether the data is in sync with the server (and "Sync now"), and which Lyrious server
	/// (host) the app talks to, checked to be a web address as it's typed. Its ✓ (right after it)
	/// checks the server answers before switching. A host that
	/// doesn't answer can still be used: a warning says so, and pressing ✓ again uses it. Something
	/// that isn't an address at all is refused. ✕ (at the top) goes back.
	/// </summary>
	public sealed class SyncView : UserControl
	{
		private const string CancelIcon = "M6,6 L18,18 M18,6 L6,18";

		private readonly TextBox host;
		private readonly ISetlistService? sync;
		private readonly TextBlock statusTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
		private readonly TextBlock statusText = new() { Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
		private readonly TextBlock problemText = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };

		/// <summary>Raised when the user cancels.</summary>
		public event Action? Cancelled;

		/// <summary>Raised with the host's (tidied-up) address when the user wants to use it.</summary>
		public event Action<string>? Saved;

		/// <param name="check">Checks an address (see <see cref="IAuthService.CheckServerAsync"/>).</param>
		/// <param name="sync">The data sync, to show its status and sync now; null to leave that out.</param>
		public SyncView(string currentHost, Func<string, Task<ServerCheck>> check, ISetlistService? sync = null)
		{
			this.sync = sync;
			host = new TextBox { Text = currentHost, PlaceholderText = L.T("e.g. https://lyrious.se"), VerticalContentAlignment = VerticalAlignment.Center };

			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));

			// The address last warned about: pressing ✓ again for the same address uses it anyway.
			string? warnedAbout = null;

			var saveButton = PageLayout.OkButton(L.T("Save host"));
			var cancelButton = EscapeKey.MarkAsClose(HomeView.IconButton(CancelIcon, L.T("Cancel")));
			cancelButton.Click += (_, _) => Cancelled?.Invoke();
			// Checked as it's typed: something that isn't a web address can't be saved, and says why.
			// (Whether the server answers is checked when ✓ is pressed.)
			void CheckTyped()
			{
				var problem = AddressProblem(host.Text);
				saveButton.IsEnabled = problem == null;
				// The ✓ only once the host has been changed: there's nothing to save before.
				saveButton.IsVisible = !string.Equals(host.Text?.Trim(), currentHost.Trim(), StringComparison.OrdinalIgnoreCase);
				if (problem != null)
				{
					warnedAbout = null;
					error.Text = problem;
					ShowAs(error, LyriousTheme.ErrorBrush);
					error.IsVisible = true;
					ToolTip.SetTip(saveButton, L.T("Save host"));
				}
				else if (warnedAbout == null || warnedAbout != host.Text?.Trim())
				{
					// A different address is checked afresh.
					warnedAbout = null;
					error.IsVisible = false;
					ToolTip.SetTip(saveButton, L.T("Save host"));
				}
			}

			host.TextChanged += (_, _) => CheckTyped();
			saveButton.IsVisible = false; // Nothing changed yet.

			async Task SaveAsync()
			{
				if (!saveButton.IsEnabled)
				{
					return;
				}

				var address = host.Text!.Trim();
				if (warnedAbout == address)
				{
					// Warned already, and ✓ pressed again: use it anyway.
					Saved?.Invoke(address);
					return;
				}

				saveButton.IsEnabled = cancelButton.IsEnabled = host.IsEnabled = false;
				error.IsVisible = false;
				try
				{
					var result = await check(address);
					if (result is { Address: { } usable, Answers: true })
					{
						Saved?.Invoke(usable);
						return;
					}

					if (result.Address == null)
					{
						error.Text = result.Message; // Not an address at all: can't be used.
						ShowAs(error, LyriousTheme.ErrorBrush);
					}
					else
					{
						warnedAbout = address;
						ShowAs(error, LyriousTheme.WarningBrush);
						error.Text = L.T("Warning: {0} You can use it anyway: press ✓ again. Until it answers you'll work offline, and you can only log in if you've logged in to it on this device before.", result.Message);
						ToolTip.SetTip(saveButton, L.T("Use this host anyway"));
					}

					error.IsVisible = true;
				}
				catch (Exception ex)
				{
					error.Text = ex.Message;
					ShowAs(error, LyriousTheme.ErrorBrush);
					error.IsVisible = true;
				}

				saveButton.IsEnabled = cancelButton.IsEnabled = host.IsEnabled = true;
			}

			saveButton.Click += (_, _) => _ = SaveAsync();
			host.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Enter)
				{
					e.Handled = true;
					_ = SaveAsync();
				}
			};

			var syncNow = new Button { Content = L.T("Sync now"), HorizontalAlignment = HorizontalAlignment.Left };
			syncNow.Click += async (_, _) =>
			{
				syncNow.IsEnabled = false;
				try
				{
					await sync!.SyncNowAsync();
				}
				finally
				{
					syncNow.IsEnabled = true;
					ShowStatus();
				}
			};
			problemText.Bind(TextBlock.ForegroundProperty, problemText.GetResourceObservable(LyriousTheme.ErrorBrush));

			var form = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					new StackPanel
					{
						Spacing = 8,
						IsVisible = sync != null,
						Margin = new Thickness(0, 0, 0, 16),
						Children = { statusTitle, statusText, problemText, syncNow },
					},
					// The host, with ✓ right after it to save just that.
					new StackPanel { Spacing = 4, Children = { new TextBlock { Text = L.T("Host"), FontSize = 13, Opacity = 0.75 }, HostRow(host, saveButton) } },
					error,
					new TextBlock
					{
						Text = L.T("Changing the host logs you out; log in again on the new server."),
						Opacity = 0.7,
						TextWrapping = TextWrapping.Wrap,
					},
				},
			};

			// Only where the app can listen on the network (the desktop app).
			if (App.LanSharing is { } sharing)
			{
				form.Children.Add(LanSharingSetting(sharing, currentHost));
			}

			// Scrolls when it doesn't fit (a short window or a large interface size).
			form.Margin = new Thickness(0, 0, 0, 16);
			var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			Content = PageLayout.Page(
				L.T("Sync"),
				scroller,
				subtitle: L.T("The Lyrious server this app syncs with"),
				titleAction: cancelButton,
				icon: AppIcons.Sync);
		}

		/// <summary>
		/// Sharing on the local network (<see cref="ILanSharing"/>): on and off, the port, and the
		/// addresses to open on the other devices, or why it couldn't be started. These apply at
		/// once (not with ✓, which is for the host) and are remembered, so the app shares again the
		/// next time it starts.
		/// </summary>
		private static Control LanSharingSetting(ILanSharing sharing, string currentHost)
		{
			var toggle = new ToggleSwitch { IsChecked = LanSharingSettings.Enabled, OnContent = L.T("On"), OffContent = L.T("Off"), VerticalAlignment = VerticalAlignment.Center };
			var port = new NumericUpDown
			{
				Value = LanSharingSettings.Port,
				Minimum = LanSharingSettings.MinPort,
				Maximum = LanSharingSettings.MaxPort,
				Increment = 1,
				FormatString = "0",
				MinWidth = 140,
				HorizontalAlignment = HorizontalAlignment.Left,
			};
			// Selectable, to copy an address.
			var status = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };

			// One at a time, and the last choice wins: a change made while starting is applied after.
			var applying = false;
			var again = false;
			async Task ApplyAsync()
			{
				if (applying)
				{
					again = true;
					return;
				}

				applying = true;
				do
				{
					again = false;
					var on = toggle.IsChecked == true;
					LanSharingSettings.Enabled = on;
					if (port.Value is { } chosen)
					{
						LanSharingSettings.Port = (int)chosen;
					}

					try
					{
						if (on)
						{
							await sharing.StartAsync(LanSharingSettings.Port);
							var addresses = sharing.Addresses;
							status.Text = addresses.Count > 0
								? L.T("Open this in a browser on the other device:\n") + string.Join("\n", addresses)
								: L.T("This computer isn't on a network right now.");
						}
						else
						{
							await sharing.StopAsync();
							status.Text = "";
						}
					}
					catch (Exception ex)
					{
						status.Text = ex.Message;
					}

					status.IsVisible = !string.IsNullOrEmpty(status.Text);
				}
				while (again);

				applying = false;
			}

			toggle.IsCheckedChanged += (_, _) => _ = ApplyAsync();
			port.ValueChanged += (_, _) => _ = ApplyAsync();
			if (toggle.IsChecked == true)
			{
				// Already on: shows the addresses (or tries again, if it couldn't be started).
				_ = ApplyAsync();
			}

			DockPanel.SetDock(toggle, Dock.Right);
			toggle.Margin = new Thickness(16, 0, 0, 0);
			return new StackPanel
			{
				Spacing = 8,
				Margin = new Thickness(0, 24, 0, 0),
				Children =
				{
					new TextBlock { Text = L.T("Local network"), FontSize = 18, FontWeight = FontWeight.SemiBold },
					new DockPanel
					{
						Children =
						{
							toggle,
							new StackPanel
							{
								Spacing = 4,
								Children =
								{
									new TextBlock { Text = L.T("Share on the local network"), FontSize = 16, TextWrapping = TextWrapping.Wrap },
									new TextBlock
									{
										Text = L.T("Lets phones and other devices on the same network open Lyrious in a browser, through this computer. They use the host above ({0}), even one only this computer can reach.", currentHost),
										Opacity = 0.7,
										TextWrapping = TextWrapping.Wrap,
									},
								},
							},
						},
					},
					new StackPanel { Spacing = 4, Children = { new TextBlock { Text = L.T("Port"), FontSize = 13, Opacity = 0.75 }, port } },
					status,
				},
			};
		}

		/// <summary>The host box with its ✓ right after it.</summary>
		private static Control HostRow(TextBox box, Button save)
		{
			save.Margin = new Thickness(8, 0, 0, 0);
			save.VerticalAlignment = VerticalAlignment.Center;
			DockPanel.SetDock(save, Dock.Right);
			return new DockPanel { Children = { save, box } };
		}

		/// <summary>
		/// Why the text isn't a web address the app can use (as the server check would say), or null
		/// when it is one: "http://" may be left out, as when it's checked.
		/// </summary>
		private static string? AddressProblem(string? text)
		{
			var address = text?.Trim() ?? "";
			if (address.Length == 0)
			{
				return L.T("Type the server's address, e.g. https://lyrious.se.");
			}

			if (!address.Contains("://"))
			{
				address = "http://" + address;
			}

			return Uri.TryCreate(address, UriKind.Absolute, out var uri)
			       && uri.Scheme is "http" or "https"
			       && uri.Host.Length > 0
			       && !address.Contains(' ')
				? null
				: L.T("That isn't a web address. Use for example https://lyrious.se.");
		}

		protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
		{
			base.OnLoaded(e);
			if (sync != null)
			{
				sync.StatusChanged += OnStatusChanged;
				ShowStatus();
			}
		}

		protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
		{
			base.OnUnloaded(e);
			if (sync != null)
			{
				sync.StatusChanged -= OnStatusChanged;
			}
		}

		/// <summary>Colours a message: red for errors, yellow for warnings (following the theme).</summary>
		private static void ShowAs(TextBlock message, string brush)
			=> message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(brush));

		private void OnStatusChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(ShowStatus);

		private void ShowStatus()
		{
			if (sync == null)
			{
				return;
			}

			var status = sync.Status;
			statusTitle.Text = SyncStatusText.Short(status);
			statusText.Text = SyncStatusText.Long(status);
			problemText.Text = status.Problem;
			problemText.IsVisible = status.Problem != null;
		}
	}
}
