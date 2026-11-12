using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>Changing the logged-in member's password.</summary>
	public sealed class ChangePasswordViewModel(IAuthService authService) : FormViewModel
	{
		private string currentPassword = "";
		private string newPassword = "";
		private string confirmPassword = "";

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

		protected override bool IsComplete => CurrentPassword.Length > 0 && NewPassword.Length > 0 && ConfirmPassword.Length > 0;

		public Task ChangePasswordAsync() => RunAsync(async () =>
		{
			if (NewPassword != ConfirmPassword)
			{
				ErrorMessage = "The new passwords don't match.";
				return;
			}

			if (NewPassword == CurrentPassword)
			{
				ErrorMessage = "The new password is the same as the current one.";
				return;
			}

			var result = await authService.ChangePasswordAsync(CurrentPassword, NewPassword);
			if (result.Succeeded)
			{
				CurrentPassword = NewPassword = ConfirmPassword = "";
				SuccessMessage = "Your password has been changed.";
			}
			else
			{
				ErrorMessage = result.Error ?? "Changing the password failed.";
			}
		});
	}
}
