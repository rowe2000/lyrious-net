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
	/// (host) the app talks to. Save (✓) checks the server answers before switching; Cancel (✕) goes
	/// back. If saving fails, the error is shown and the address kept.
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

		/// <summary>Raised with the host's address after it's been saved.</summary>
		public event Action<string>? Saved;

		/// <param name="save">Switches to the host; returns null when done, else a message for the user.</param>
		/// <param name="sync">The data sync, to show its status and sync now; null to leave that out.</param>
		public SyncView(string currentHost, Func<string, Task<string?>> save, ISetlistService? sync = null)
		{
			this.sync = sync;
			host = new TextBox { Text = currentHost, PlaceholderText = "e.g. http://localhost:5180" };

			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));

			var saveButton = HomeView.IconButton(SaveIcon, "Save host");
			var cancelButton = HomeView.IconButton(CancelIcon, "Cancel");
			cancelButton.Click += (_, _) => Cancelled?.Invoke();
			host.TextChanged += (_, _) => saveButton.IsEnabled = !string.IsNullOrWhiteSpace(host.Text);

			async Task SaveAsync()
			{
				if (!saveButton.IsEnabled)
				{
					return;
				}

				var address = host.Text!.Trim();
				saveButton.IsEnabled = cancelButton.IsEnabled = host.IsEnabled = false;
				error.IsVisible = false;
				try
				{
					if (await save(address) is { } problem)
					{
						error.Text = problem;
						error.IsVisible = true;
					}
					else
					{
						Saved?.Invoke(address);
						return;
					}
				}
				catch (Exception ex)
				{
					error.Text = ex.Message;
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
