using System;
using System.Linq;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>Second step of resetting a forgotten password: the emailed code and a new password.</summary>
	public sealed class ResetPasswordViewModel(IAuthService authService, string userNameOrEmail) : FormViewModel
	{
		private string code = "";
		private string newPassword = "";
		private string confirmPassword = "";

		/// <summary>Raised after the password has been reset, with the username or email used.</summary>
		public event Action<string>? PasswordReset;

		public string UserNameOrEmail { get; } = userNameOrEmail;

		public string Code
		{
			get => code;
			set => Set(ref code, value);
		}

		public string NewPassword
		{
			get => newPassword;
			set => Set(ref newPassword, value);
		}

		public string ConfirmPassword
		{
			get => confirmPassword;
			set => Set(ref confirmPassword, value);
		}

		protected override bool IsComplete => !string.IsNullOrWhiteSpace(Code) && NewPassword.Length > 0 && ConfirmPassword.Length > 0;

		public Task ResetAsync() => RunAsync(async () =>
		{
			var trimmedCode = new string(Code.Where(c => !char.IsWhiteSpace(c)).ToArray());
			if (!trimmedCode.All(char.IsDigit))
			{
				ErrorMessage = "The code is the 6 digits from the email.";
				return;
			}

			if (NewPassword != ConfirmPassword)
			{
				ErrorMessage = "The new passwords don't match.";
				return;
			}

			var result = await authService.ResetPasswordAsync(UserNameOrEmail, trimmedCode, NewPassword);
			if (result.Succeeded)
			{
				NewPassword = ConfirmPassword = Code = "";
				PasswordReset?.Invoke(UserNameOrEmail);
			}
			else
			{
				ErrorMessage = result.Error ?? "Resetting the password failed.";
			}
		});
	}
}
