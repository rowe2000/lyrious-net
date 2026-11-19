using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>Asks for a password reset code to be emailed.</summary>
	public sealed class ForgotPasswordView : FormPage
	{
		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		public ForgotPasswordView(ForgotPasswordViewModel viewModel)
			: base(viewModel, L.T("Forgot password"), L.T("Enter your username or email and we'll email you a code to choose a new password."), L.T("Send code"), viewModel.SendCodeAsync)
		{
			AddField(L.T("Username or email"), () => viewModel.UserNameOrEmail, value => viewModel.UserNameOrEmail = value);
			AddLink(L.T("Back to login"), () => LoginRequested?.Invoke());
		}
	}
}
