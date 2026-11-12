using System;

namespace Lyrious.UILib.Login
{
	/// <summary>Asks for a password reset code to be emailed.</summary>
	public sealed class ForgotPasswordView : FormPage
	{
		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		public ForgotPasswordView(ForgotPasswordViewModel viewModel)
			: base(viewModel, "Forgot password", "Enter your username or email and we'll email you a code to choose a new password.", "Send code", viewModel.SendCodeAsync)
		{
			AddField("Username or email", () => viewModel.UserNameOrEmail, value => viewModel.UserNameOrEmail = value);
			AddLink("Back to login", () => LoginRequested?.Invoke());
		}
	}
}
