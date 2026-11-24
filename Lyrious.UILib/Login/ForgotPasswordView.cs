using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>Asks for a password reset code: by SMS when a phone number is entered, else by email.</summary>
	public sealed class ForgotPasswordView : FormPage
	{
		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		public ForgotPasswordView(ForgotPasswordViewModel viewModel)
			: base(viewModel, L.T("Forgot password"), L.T("Enter your username, email or phone number, and we'll send you a code to choose a new password: by SMS if you enter your phone number, else by email."), L.T("Send code"), viewModel.SendCodeAsync)
		{
			AddField(L.T("Username, email or phone"), () => viewModel.UserNameOrEmail, value => viewModel.UserNameOrEmail = value);
			AddLink(L.T("Back to login"), () => LoginRequested?.Invoke());
		}
	}
}
