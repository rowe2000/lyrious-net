using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>Sets a new password with the emailed code.</summary>
	public sealed class ResetPasswordView : FormPage
	{
		/// <summary>Raised when the user wants a new code.</summary>
		public event Action? NewCodeRequested;

		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		public ResetPasswordView(ResetPasswordViewModel viewModel)
			: base(
				viewModel,
				L.T("Choose a new password"),
				// The server doesn't reveal whether the account exists or has an email address.
				L.T("If {0} has an account with an email address, we've sent it a 6-digit code. It expires in a few minutes.", viewModel.UserNameOrEmail),
				L.T("Reset password"),
				viewModel.ResetAsync)
		{
			AddField(L.T("Code from the email"), () => viewModel.Code, value => viewModel.Code = value);
			AddField(L.T("New password"), () => viewModel.NewPassword, value => viewModel.NewPassword = value, isPassword: true);
			AddField(L.T("Confirm new password"), () => viewModel.ConfirmPassword, value => viewModel.ConfirmPassword = value, isPassword: true);
			AddLink(L.T("Send a new code"), () => NewCodeRequested?.Invoke());
			AddLink(L.T("Back to login"), () => LoginRequested?.Invoke());
		}
	}
}
