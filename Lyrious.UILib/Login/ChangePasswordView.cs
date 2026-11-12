using System;

namespace Lyrious.UILib.Login
{
	/// <summary>Changes the logged-in member's password.</summary>
	public sealed class ChangePasswordView : FormPage
	{
		/// <summary>Raised when the user is done and wants to go back.</summary>
		public event Action? BackRequested;

		public ChangePasswordView(ChangePasswordViewModel viewModel)
			: base(viewModel, "Change password", "Enter your current password and choose a new one.", "Change password", viewModel.ChangePasswordAsync)
		{
			AddField("Current password", () => viewModel.CurrentPassword, value => viewModel.CurrentPassword = value, isPassword: true);
			AddField("New password", () => viewModel.NewPassword, value => viewModel.NewPassword = value, isPassword: true);
			AddField("Confirm new password", () => viewModel.ConfirmPassword, value => viewModel.ConfirmPassword = value, isPassword: true);
			AddLink("Back", () => BackRequested?.Invoke());
		}
	}
}
