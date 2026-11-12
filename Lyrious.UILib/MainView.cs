using Avalonia.Controls;
using Lyrious.UILib.Login;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's main content. Desktop shows it inside <see cref="MainWindow"/>; single-view hosts
	/// such as the browser, which have no windows, show it directly. It starts with onboarding
	/// (login, or creating an account), then shows the signed-in app (<see cref="HomeView"/>).
	/// </summary>
	public class MainView : UserControl
	{
		public MainView()
		{
			ShowLogin();
		}

		private void ShowLogin() => ShowLogin(null, null);

		/// <param name="notice">A confirmation to show above the form, e.g. after a password reset.</param>
		/// <param name="userName">Prefills the username field.</param>
		private void ShowLogin(string? notice, string? userName)
		{
			var login = new LoginViewModel(App.AuthService);
			login.LoggedIn += ShowHome;
			if (notice != null)
			{
				login.ShowNotice(notice, userName);
			}

			var view = new LoginView(login);
			view.RegisterRequested += ShowRegister;
			view.ForgotPasswordRequested += () => ShowForgotPassword(login.UserName);
			Content = view;
		}

		private void ShowForgotPassword(string userNameOrEmail)
		{
			var forgot = new ForgotPasswordViewModel(App.AuthService, userNameOrEmail);
			forgot.CodeSent += ShowResetPassword;
			var view = new ForgotPasswordView(forgot);
			view.LoginRequested += ShowLogin;
			Content = view;
		}

		private void ShowResetPassword(string userNameOrEmail)
		{
			var reset = new ResetPasswordViewModel(App.AuthService, userNameOrEmail);
			// Prefill the username only when the user typed one (not an email address).
			reset.PasswordReset += value => ShowLogin(
				"Your password has been reset. Log in with your new password.",
				value.Contains('@') ? null : value);
			var view = new ResetPasswordView(reset);
			view.NewCodeRequested += () => ShowForgotPassword(userNameOrEmail);
			view.LoginRequested += ShowLogin;
			Content = view;
		}

		private void ShowRegister()
		{
			var register = new RegisterViewModel(App.AuthService);
			register.LoggedIn += ShowHome;
			var view = new RegisterView(register);
			view.LoginRequested += ShowLogin;
			Content = view;
		}

		private void ShowHome(AuthenticatedUser user)
		{
			var home = new HomeView(user, App.SetlistService);
			home.ChangePasswordRequested += () => ShowChangePassword(home);
			home.LogOutRequested += () =>
			{
				App.AuthService.LogOut();
				ShowLogin("You've been logged out.", null);
			};
			Content = home;
		}

		// Back returns to the same home view, so the page the user was on is kept.
		private void ShowChangePassword(HomeView home)
		{
			var view = new ChangePasswordView(new ChangePasswordViewModel(App.AuthService));
			view.BackRequested += () => Content = home;
			Content = view;
		}
	}
}
