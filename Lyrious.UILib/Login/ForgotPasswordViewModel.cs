using System;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>First step of resetting a forgotten password: ask for a code to be emailed.</summary>
	public sealed class ForgotPasswordViewModel(IAuthService authService, string userNameOrEmail = "") : FormViewModel
	{
		private string userNameOrEmail = userNameOrEmail;

		/// <summary>Raised after the code has been requested, with the username or email used.</summary>
		public event Action<string>? CodeSent;

		public string UserNameOrEmail
		{
			get => userNameOrEmail;
			set => Set(ref userNameOrEmail, value);
		}

		protected override bool IsComplete => !string.IsNullOrWhiteSpace(UserNameOrEmail);

		public Task SendCodeAsync() => RunAsync(async () =>
		{
			var value = UserNameOrEmail.Trim();
			var result = await authService.RequestPasswordResetAsync(value);
			if (result.Succeeded)
			{
				CodeSent?.Invoke(value);
			}
			else
			{
				ErrorMessage = result.Error ?? "Couldn't send a code.";
			}
		});
	}
}
