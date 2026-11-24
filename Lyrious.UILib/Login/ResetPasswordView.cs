using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>Sets a new password with the code sent by email or SMS.</summary>
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
				// The server doesn't reveal whether the account exists or can be reached.
				L.T("If {0} has an account we can reach, we've sent a 6-digit code: by SMS to a phone number, else by email. It expires in a few minutes.", viewModel.UserNameOrEmail),
				L.T("Reset password"),
				viewModel.ResetAsync)
		{
			AddField(L.T("Code from the email or SMS"), () => viewModel.Code, value => viewModel.Code = value);
			AddField(L.T("New password"), () => viewModel.NewPassword, value => viewModel.NewPassword = value, isPassword: true);
			AddControl(Setlists.PageLayout.PasswordRules());
			AddField(L.T("Confirm new password"), () => viewModel.ConfirmPassword, value => viewModel.ConfirmPassword = value, isPassword: true);
			AddLink(L.T("Send a new code"), () => NewCodeRequested?.Invoke());
			AddLink(L.T("Back to login"), () => LoginRequested?.Invoke());
		}
	}
}
