using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Lyrious.UILib.Login;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's main content. Desktop shows it inside <see cref="MainWindow"/>; single-view hosts
	/// such as the browser, which have no windows, show it directly. It starts with onboarding
	/// (login, or creating an account), then shows the signed-in app (<see cref="HomeView"/>).
	/// Where the platform has a password store, it offers to save the password after login, logs in
	/// with the saved password when the app starts, and fills in the login form from it.
	/// </summary>
	public class MainView : UserControl
	{
		// The credential last read from (or saved to) the password store, to avoid offering to save
		// what's already saved and to know when a saved password belongs to the logged-in member.
		private SavedCredential? savedCredential;

		public MainView()
		{
			_ = StartAsync();
		}

		/// <summary>
		/// At app start, logs in with the saved password if there is one, so the login form is
		/// skipped. If that fails, the form is shown with the reason and the saved login filled in.
		/// Only done at start: after logging out, the form is shown as usual.
		/// </summary>
		private async Task StartAsync()
		{
			Content = Splash("Logging in…");

			SavedCredential? saved;
			try
			{
				saved = await App.CredentialStore.LoadAsync();
			}
			catch (Exception)
			{
				saved = null; // A password store problem must never stop anyone from logging in by hand.
			}

			if (saved == null)
			{
				ShowLogin();
				return;
			}

			savedCredential = saved;
			string error;
			try
			{
				var result = await App.AuthService.LoginAsync(saved.UserName, saved.Password);
				if (result.User is { } user)
				{
					ShowHome(user, saved);
					return;
				}

				error = result.Error ?? "Login failed.";
			}
			catch (Exception ex)
			{
				error = $"Couldn't reach the server. {ex.Message}";
			}

			var login = ShowLogin(null, null, fillInSaved: false);
			login.UserName = saved.UserName;
			login.Password = saved.Password;
			login.ShowError($"Couldn't log in with the saved password. {error}");
		}

		private static Control Splash(string text) => new StackPanel
		{
			Spacing = 12,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Children =
			{
				new TextBlock { Text = text, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center },
				new ProgressBar { IsIndeterminate = true, Width = 200 },
			},
		};

		private void ShowLogin() => ShowLogin(null, null);

		/// <param name="notice">A confirmation to show above the form, e.g. after a password reset.</param>
		/// <param name="userName">Prefills the username field (and then no saved password is filled in).</param>
		/// <param name="fillInSaved">Whether to fill in the saved password (when no username is given).</param>
		private LoginViewModel ShowLogin(string? notice, string? userName, bool fillInSaved = true)
		{
			var login = new LoginViewModel(App.AuthService);
			login.LoggedIn += (user, credential) => _ = AfterLoginAsync(user, credential);
			if (notice != null)
			{
				login.ShowNotice(notice, userName);
			}

			var view = new LoginView(login, App.AuthService.ServerAddress);
			view.HostRequested += ShowHostFromLogin;
			view.RegisterRequested += ShowRegister;
			view.ForgotPasswordRequested += () => ShowForgotPassword(login.UserName);
			Content = view;

			// After a password reset the saved password is out of date, so it's not filled in then.
			if (userName == null && fillInSaved)
			{
				_ = FillInSavedPasswordAsync(login);
			}

			return login;
		}

		private async Task FillInSavedPasswordAsync(LoginViewModel login)
		{
			try
			{
				savedCredential = await App.CredentialStore.LoadAsync();
			}
			catch (Exception)
			{
				// A password store problem must never stop anyone from logging in by hand.
				savedCredential = null;
				return;
			}

			// Don't overwrite what the user started typing while the store was being read.
			if (savedCredential != null && login.UserName.Length == 0 && login.Password.Length == 0)
			{
				login.UserName = savedCredential.UserName;
				login.Password = savedCredential.Password;
			}
		}

		/// <summary>Offers to save the password (unless it's saved already), then shows the app.</summary>
		private async Task AfterLoginAsync(AuthenticatedUser user, SavedCredential credential)
		{
			var store = App.CredentialStore;
			if (!store.IsSupported || credential == savedCredential)
			{
				ShowHome(user, credential);
				return;
			}

			if (store.AsksUserItself)
			{
				// E.g. the browser's password manager shows its own "Save password?" prompt.
				await SaveCredentialAsync(credential);
				ShowHome(user, credential);
				return;
			}

			var view = new SavePasswordView(credential.UserName);
			view.Answered += save => _ = AnsweredAsync(save);
			Content = view;

			async Task AnsweredAsync(bool save)
			{
				if (save)
				{
					await SaveCredentialAsync(credential);
				}

				ShowHome(user, credential);
			}
		}

		private async Task SaveCredentialAsync(SavedCredential credential)
		{
			try
			{
				await App.CredentialStore.SaveAsync(credential);
				savedCredential = credential;
			}
			catch (Exception)
			{
				// Not being able to save the password isn't worth interrupting the login for.
			}
		}

		private bool IsSavedFor(string userName)
			=> savedCredential != null && string.Equals(savedCredential.UserName, userName, StringComparison.OrdinalIgnoreCase);

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
			register.LoggedIn += (user, credential) => _ = AfterLoginAsync(user, credential);
			var view = new RegisterView(register);
			view.LoginRequested += ShowLogin;
			Content = view;
		}

		private void ShowHome(AuthenticatedUser user, SavedCredential credential)
		{
			var store = App.CredentialStore;
			var home = new HomeView(user, App.SetlistService, offerForgetSavedPassword: store.CanForget && IsSavedFor(credential.UserName));
			home.ChangePasswordRequested += () => ShowChangePassword(home, credential.UserName);
			home.ForgetSavedPasswordRequested += () => _ = ForgetSavedPasswordAsync();
			home.SyncRequested += () => ShowSync(home);
			home.LogOutRequested += () =>
			{
				App.AuthService.LogOut();
				ShowLogin("You've been logged out.", null);
			};
			Content = home;
		}

		private async Task ForgetSavedPasswordAsync()
		{
			try
			{
				await App.CredentialStore.ForgetAsync();
			}
			catch (Exception)
			{
				// Nothing more the app can do; the item is gone from the menu either way.
			}

			savedCredential = null;
		}

		/// <summary>
		/// The Sync page: the server (host) the app talks to. A new host is remembered on this device
		/// and, since the login belongs to the old server, leads back to the login page.
		/// </summary>
		private void ShowSync(HomeView home)
		{
			var auth = App.AuthService;
			var current = auth.ServerAddress;
			// The current host needs no check; another is checked, and used even if it doesn't answer
			// (the Sync page warns first).
			var view = new SyncView(current, address => SameHost(address, current)
				? Task.FromResult(new ServerCheck(current, true, null))
				: auth.CheckServerAsync(address), App.SetlistService);
			view.Cancelled += () => Content = home;
			view.Saved += address =>
			{
				if (SameHost(address, current))
				{
					Content = home; // Unchanged.
					return;
				}

				auth.UseServer(Tidy(address));
				App.Settings.Set(SettingKeys.Host, auth.ServerAddress);
				home.Close();
				ShowLogin($"Now syncing with {auth.ServerAddress}. Log in to continue.", null);
			};
			Content = view;
		}

		/// <summary>
		/// The host page, from the login page: there's no session yet, so it's only the address (no
		/// sync status). A new host is remembered on this device; back to the login page either way.
		/// </summary>
		private void ShowHostFromLogin()
		{
			var auth = App.AuthService;
			var current = auth.ServerAddress;
			var view = new SyncView(current, address => SameHost(address, current)
				? Task.FromResult(new ServerCheck(current, true, null))
				: auth.CheckServerAsync(address));
			view.Cancelled += ShowLogin;
			view.Saved += address =>
			{
				if (SameHost(address, current))
				{
					ShowLogin();
					return;
				}

				auth.UseServer(Tidy(address));
				App.Settings.Set(SettingKeys.Host, auth.ServerAddress);
				ShowLogin($"Now using {auth.ServerAddress}.", null);
			};
			Content = view;
		}

		private static bool SameHost(string address, string current)
			=> string.Equals(Tidy(address), current.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

		/// <summary>An address as the auth service keeps it: with a scheme ("http://" if none) and no trailing slash.</summary>
		private static string Tidy(string address)
		{
			var text = address.Trim().TrimEnd('/');
			return text.Contains("://") ? text : "http://" + text;
		}

		// Back returns to the same home view, so the page the user was on is kept.
		private void ShowChangePassword(HomeView home, string userName)
		{
			var changePassword = new ChangePasswordViewModel(App.AuthService);
			// Keep a saved password working: replace it with the new one.
			changePassword.PasswordChanged += newPassword =>
			{
				if (IsSavedFor(userName))
				{
					_ = SaveCredentialAsync(new SavedCredential(savedCredential!.UserName, newPassword));
				}
			};

			var view = new ChangePasswordView(changePassword);
			view.BackRequested += () => Content = home;
			Content = view;
		}
	}
}
