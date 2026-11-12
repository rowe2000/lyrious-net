using System;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>Registering a new member; logs in right after a successful registration.</summary>
	public sealed class RegisterViewModel(IAuthService authService) : FormViewModel
	{
		private string userName = "";
		private string displayName = "";
		private string email = "";
		private string password = "";
		private string confirmPassword = "";

		/// <summary>Raised when the new member has been registered and logged in.</summary>
		public event Action<AuthenticatedUser>? LoggedIn;

		public string UserName
		{
			get => userName;
			set => Set(ref userName, value);
		}

		public string DisplayName
		{
			get => displayName;
			set => Set(ref displayName, value);
		}

		/// <summary>Optional.</summary>
		public string Email
		{
			get => email;
			set => Set(ref email, value);
		}

		public string Password
		{
			get => password;
			set => Set(ref password, value);
		}

		public string ConfirmPassword
		{
			get => confirmPassword;
			set => Set(ref confirmPassword, value);
		}

		protected override bool IsComplete =>
			!string.IsNullOrWhiteSpace(UserName)
			&& !string.IsNullOrWhiteSpace(DisplayName)
			&& Password.Length > 0
			&& ConfirmPassword.Length > 0;

		public Task RegisterAsync() => RunAsync(async () =>
		{
			if (Password != ConfirmPassword)
			{
				ErrorMessage = "The passwords don't match.";
				return;
			}

			var trimmedEmail = Email.Trim();
			if (trimmedEmail.Length > 0 && !trimmedEmail.Contains('@'))
			{
				ErrorMessage = "That doesn't look like an email address.";
				return;
			}

			// The server enforces the password rules and unique usernames, and explains what's wrong.
			var registration = new Registration(UserName.Trim(), Password, DisplayName.Trim(), trimmedEmail.Length > 0 ? trimmedEmail : null);
			var registered = await authService.RegisterAsync(registration);
			if (!registered.Succeeded)
			{
				ErrorMessage = registered.Error ?? "Registration failed.";
				return;
			}

			var login = await authService.LoginAsync(registration.UserName, Password);
			if (login.User is { } user)
			{
				Password = ConfirmPassword = "";
				LoggedIn?.Invoke(user);
			}
			else
			{
				ErrorMessage = $"Your account was created, but logging in failed: {login.Error}";
			}
		});
	}
}
