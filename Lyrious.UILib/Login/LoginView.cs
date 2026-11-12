using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// The first page of onboarding: sign in with username and password. Code-only; the state
	/// lives in <see cref="LoginViewModel"/> and is mirrored into the controls by <see cref="Sync"/>.
	/// </summary>
	public sealed class LoginView : UserControl
	{
		private readonly LoginViewModel viewModel;
		private readonly TextBox userNameBox;
		private readonly TextBox passwordBox;
		private readonly TextBlock errorText;
		private readonly ProgressBar busyBar;
		private readonly Button logInButton;

		public LoginView(LoginViewModel viewModel)
		{
			this.viewModel = viewModel;
			DataContext = viewModel;

			var title = new TextBlock
			{
				Text = "Lyrious",
				FontSize = 36,
				FontWeight = FontWeight.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
			};

			var tagline = new TextBlock
			{
				Text = "Songs, setlists and lyrics for your band, in sync.",
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center,
				Opacity = 0.7,
				Margin = new Thickness(0, 0, 0, 16),
			};

			userNameBox = new TextBox
			{
				PlaceholderText = "Username",
				Text = viewModel.UserName,
			};

			passwordBox = new TextBox
			{
				PlaceholderText = "Password",
				PasswordChar = '•',
				Text = viewModel.Password,
			};
			passwordBox.Classes.Add("revealPasswordButton");

			errorText = new TextBlock
			{
				TextWrapping = TextWrapping.Wrap,
				IsVisible = false,
			};
			errorText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(LyriousTheme.ErrorBrush));

			busyBar = new ProgressBar
			{
				IsIndeterminate = true,
				IsVisible = false,
			};

			logInButton = new Button
			{
				Content = "Log in",
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				// Enter anywhere on the page logs in.
				IsDefault = true,
			};
			logInButton.Classes.Add("accent");

			var card = new Border
			{
				MaxWidth = 360,
				Margin = new Thickness(16),
				Padding = new Thickness(32),
				CornerRadius = new CornerRadius(8),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Child = new StackPanel
				{
					Spacing = 12,
					Children = { title, tagline, userNameBox, passwordBox, errorText, busyBar, logInButton },
				},
			};
			card.Bind(Border.BackgroundProperty, this.GetResourceObservable(LyriousTheme.CardBrush));

			// Scrollable, so the form stays reachable on small screens with the on-screen keyboard open.
			Content = new ScrollViewer { Content = card };

			userNameBox.TextChanged += (_, _) => viewModel.UserName = userNameBox.Text ?? "";
			passwordBox.TextChanged += (_, _) => viewModel.Password = passwordBox.Text ?? "";
			logInButton.Click += async (_, _) => await viewModel.LogInAsync();
			viewModel.PropertyChanged += (_, _) => Sync();
			Sync();
		}

		protected override void OnLoaded(RoutedEventArgs e)
		{
			base.OnLoaded(e);
			userNameBox.Focus();
		}

		private void Sync()
		{
			logInButton.IsEnabled = viewModel.CanLogIn;
			userNameBox.IsEnabled = passwordBox.IsEnabled = !viewModel.IsBusy;
			busyBar.IsVisible = viewModel.IsBusy;
			errorText.Text = viewModel.ErrorMessage;
			errorText.IsVisible = !string.IsNullOrEmpty(viewModel.ErrorMessage);

			// The view model clears the password after a successful login.
			if (passwordBox.Text != viewModel.Password)
			{
				passwordBox.Text = viewModel.Password;
			}
		}
	}
}
