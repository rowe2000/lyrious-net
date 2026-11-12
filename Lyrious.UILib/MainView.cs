using Avalonia.Controls;
using Avalonia.Layout;
using Lyrious.UILib.Login;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's main content. Desktop shows it inside <see cref="MainWindow"/>; single-view hosts
	/// such as the browser, which have no windows, show it directly. It starts with onboarding (login).
	/// </summary>
	public class MainView : UserControl
	{
		public MainView()
		{
			ShowLogin();
		}

		private void ShowLogin()
		{
			var login = new LoginViewModel(App.AuthService);
			login.LoggedIn += ShowWelcome;
			Content = new LoginView(login);
		}

		// Placeholder for the signed-in experience.
		private void ShowWelcome(AuthenticatedUser user)
		{
			Content = new TextBlock
			{
				Text = $"Welcome, {user.DisplayName}!",
				FontSize = 24,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
		}
	}
}
