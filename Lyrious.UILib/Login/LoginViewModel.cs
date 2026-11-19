using System;
using System.Threading.Tasks;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>State and behaviour of the login page, independent of how it's drawn.</summary>
	public sealed class LoginViewModel(IAuthService authService) : FormViewModel
	{
		private string userName = "";
		private string password = "";

		/// <summary>Raised after a successful login, with the credentials that worked (e.g. to offer saving them).</summary>
		public event Action<AuthenticatedUser, SavedCredential>? LoggedIn;

		public string UserName
		{
			get => userName;
			set => Set(ref userName, value);
		}

		public string Password
		{
			get => password;
			set => Set(ref password, value);
		}

		protected override bool IsComplete => !string.IsNullOrWhiteSpace(UserName) && Password.Length > 0;

		/// <summary>Shows a confirmation above the form, e.g. after a password reset, with the username filled in.</summary>
		public void ShowNotice(string message, string? userName = null)
		{
			if (userName != null)
			{
				UserName = userName;
			}

			SuccessMessage = message;
		}

		/// <summary>Shows an error above the form, e.g. when logging in with the saved password failed.</summary>
		public void ShowError(string message) => ErrorMessage = message;

		public Task LogInAsync() => RunAsync(async () =>
		{
			var credential = new SavedCredential(UserName.Trim(), Password);
			var result = await authService.LoginAsync(credential.UserName, credential.Password);
			if (result.User is { } user)
			{
				Password = "";
				LoggedIn?.Invoke(user, credential);
			}
			else
			{
				ErrorMessage = result.Error ?? L.T("Login failed.");
			}
		});
	}
}
