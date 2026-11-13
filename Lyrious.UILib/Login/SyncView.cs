using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// "Sync": whether the data is in sync with the server (and "Sync now"), and which Lyrious server
	/// (host) the app talks to. Save (✓) checks the server answers before switching. A host that
	/// doesn't answer can still be used: a warning says so, and pressing ✓ again uses it. Something
	/// that isn't an address at all is refused. Cancel (✕) goes back.
	/// </summary>
	public sealed class SyncView : UserControl
	{
		private const string SaveIcon = "M5,12 L10,17 L19,7";
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
			host = new TextBox { Text = currentHost, PlaceholderText = "e.g. http://localhost:5180" };

			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));

			// The address last warned about: pressing ✓ again for the same address uses it anyway.
			string? warnedAbout = null;

			var saveButton = HomeView.IconButton(SaveIcon, "Save host");
			var cancelButton = HomeView.IconButton(CancelIcon, "Cancel");
			cancelButton.Click += (_, _) => Cancelled?.Invoke();
			host.TextChanged += (_, _) =>
			{
				saveButton.IsEnabled = !string.IsNullOrWhiteSpace(host.Text);
				if (warnedAbout != null)
				{
					// A different address is checked afresh.
					warnedAbout = null;
					error.IsVisible = false;
					ToolTip.SetTip(saveButton, "Save host");
				}
			};

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
						error.Text = $"Warning: {result.Message} You can use it anyway: press ✓ again. Until it answers you'll work offline, and you can only log in if you've logged in to it on this device before.";
						ToolTip.SetTip(saveButton, "Use this host anyway");
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

			var syncNow = new Button { Content = "Sync now", HorizontalAlignment = HorizontalAlignment.Left };
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
					new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Host", FontSize = 13, Opacity = 0.75 }, host } },
					error,
					new TextBlock
					{
						Text = "Changing the host logs you out; log in again on the new server.",
						Opacity = 0.7,
						TextWrapping = TextWrapping.Wrap,
					},
				},
			};

			Content = PageLayout.Page(
				"Sync",
				form,
				subtitle: "The Lyrious server this app syncs with",
				titleAction: new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { saveButton, cancelButton } });
		}

		protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
		{
			base.OnLoaded(e);
			// Ready to type a new address straight away.
			host.Focus();
			host.SelectAll();
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
