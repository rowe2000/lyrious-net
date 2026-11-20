using System;
using System.Threading.Tasks;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// Registering a new member, in two steps: the details (the email address required), then the
	/// code emailed to that address, which proves it's theirs. Logs in right after a successful
	/// registration.
	/// </summary>
	public sealed class RegisterViewModel(IAuthService authService) : FormViewModel
	{
		private string userName = "";
		private string displayName = "";
		private string email = "";
		private string password = "";
		private string confirmPassword = "";
		private string code = "";
		private bool codeSent;

		/// <summary>Raised when the new member has been registered and logged in, with their credentials (e.g. to offer saving them).</summary>
		public event Action<AuthenticatedUser, SavedCredential>? LoggedIn;

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

		/// <summary>Required: the code is sent there. Changed after a code was sent, a new one is needed.</summary>
		public string Email
		{
			get => email;
			set
			{
				if (codeSent && value.Trim() != email.Trim())
				{
					CodeSent = false;
					Code = "";
				}

				Set(ref email, value);
			}
		}

		/// <summary>The code emailed to <see cref="Email"/>.</summary>
		public string Code
		{
			get => code;
			set => Set(ref code, value);
		}

		/// <summary>Whether a code has been sent to <see cref="Email"/>: the form's second step, entering it.</summary>
		public bool CodeSent
		{
			get => codeSent;
			private set => Set(ref codeSent, value);
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
			&& !string.IsNullOrWhiteSpace(Email)
			&& Password.Length > 0
			&& ConfirmPassword.Length > 0
			&& (!CodeSent || Code.Trim().Length > 0);

		/// <summary>The details checked, the code is sent (first step); with the code, the account is made (second step).</summary>
		public Task RegisterAsync() => RunAsync(async () =>
		{
			if (Password != ConfirmPassword)
			{
				ErrorMessage = L.T("The passwords don't match.");
				return;
			}

			var trimmedEmail = Email.Trim();
			if (!trimmedEmail.Contains('@'))
			{
				ErrorMessage = L.T("That doesn't look like an email address.");
				return;
			}

			if (!CodeSent)
			{
				await SendCodeNowAsync(trimmedEmail);
				return;
			}

			// The server checks the code, enforces the password rules and unique usernames, and explains what's wrong.
			var registration = new Registration(UserName.Trim(), Password, DisplayName.Trim(), trimmedEmail, Code.Trim());
			var registered = await authService.RegisterAsync(registration);
			if (!registered.Succeeded)
			{
				ErrorMessage = registered.Error ?? L.T("Registration failed.");
				return;
			}

			var credential = new SavedCredential(registration.UserName, Password);
			Code = "";
			var login = await authService.LoginAsync(credential.UserName, credential.Password);
			if (login.User is { } user)
			{
				Password = ConfirmPassword = "";
				LoggedIn?.Invoke(user, credential);
			}
			else
			{
				ErrorMessage = L.T("Your account was created, but logging in failed: {0}", login.Error);
			}
		});

		/// <summary>Sends another code to <see cref="Email"/> (e.g. the first didn't arrive).</summary>
		public Task SendNewCodeAsync() => RunStepAsync(() => SendCodeNowAsync(Email.Trim()));

		private async Task SendCodeNowAsync(string address)
		{
			var sent = await authService.SendRegistrationCodeAsync(address);
			if (!sent.Succeeded)
			{
				ErrorMessage = sent.Error ?? L.T("Couldn't send a code.");
				return;
			}

			CodeSent = true;
			SuccessMessage = L.T("A code has been sent to {0}. Enter it below to create your account.", address);
		}
	}
}
