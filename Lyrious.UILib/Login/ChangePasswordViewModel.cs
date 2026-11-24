using System.Threading.Tasks;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// Changing the logged-in member's password: the current one, the new one twice, and a code
	/// emailed to the member's address (see <see cref="SendCodeAsync"/>), which proves it's them.
	/// </summary>
	public sealed class ChangePasswordViewModel(IAuthService authService) : FormViewModel
	{
		/// <summary>Raised after the password has been changed, with the new password (e.g. to update a saved one).</summary>
		public event System.Action<string>? PasswordChanged;

		private string currentPassword = "";
		private string newPassword = "";
		private string confirmPassword = "";
		private string code = "";

		public string CurrentPassword
		{
			get => currentPassword;
			set => Set(ref currentPassword, value);
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

		/// <summary>The code emailed to the member (see <see cref="SendCodeAsync"/>).</summary>
		public string Code
		{
			get => code;
			set => Set(ref code, value);
		}

		protected override bool IsComplete => CurrentPassword.Length > 0 && NewPassword.Length > 0 && ConfirmPassword.Length > 0 && Code.Trim().Length > 0;

		/// <summary>Sends the code needed to change the password: emailed to the member's address, or texted to their phone number.</summary>
		public Task SendCodeAsync(bool bySms = false) => RunStepAsync(async () =>
		{
			var result = await authService.SendPasswordChangeCodeAsync(bySms);
			if (result.Succeeded && bySms)
			{
				SuccessMessage = L.T("A code has been texted to {0}. Enter it to change your password.", authService.PhoneNumber ?? "");
			}
			else if (result.Succeeded)
			{
				SuccessMessage = authService.Email is { } email
					? L.T("A code has been sent to {0}. Enter it to change your password.", email)
					: L.T("A code has been sent to your email address. Enter it to change your password.");
			}
			else
			{
				ErrorMessage = result.Error ?? L.T("Couldn't send a code.");
			}
		});

		public Task ChangePasswordAsync() => RunAsync(async () =>
		{
			if (NewPassword != ConfirmPassword)
			{
				ErrorMessage = L.T("The new passwords don't match.");
				return;
			}

			if (NewPassword == CurrentPassword)
			{
				ErrorMessage = L.T("The new password is the same as the current one.");
				return;
			}

			var result = await authService.ChangePasswordAsync(CurrentPassword, NewPassword, Code.Trim());
			if (result.Succeeded)
			{
				var changedTo = NewPassword;
				CurrentPassword = NewPassword = ConfirmPassword = Code = "";
				SuccessMessage = L.T("Your password has been changed.");
				PasswordChanged?.Invoke(changedTo);
			}
			else
			{
				ErrorMessage = result.Error ?? L.T("Changing the password failed.");
			}
		});
	}
}
