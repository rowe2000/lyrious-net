using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>Creates a member account; the member is logged in afterwards.</summary>
	public sealed class RegisterView : FormPage
	{
		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		public RegisterView(RegisterViewModel viewModel)
			: base(viewModel, L.T("Create account"), L.T("Join Lyrious to share songs and setlists with your band."), L.T("Create account"), viewModel.RegisterAsync)
		{
			AddField(L.T("Username"), () => viewModel.UserName, value => viewModel.UserName = value);
			AddField(L.T("Your name"), () => viewModel.DisplayName, value => viewModel.DisplayName = value);
			AddField(L.T("Email (optional)"), () => viewModel.Email, value => viewModel.Email = value);
			AddField(L.T("Password"), () => viewModel.Password, value => viewModel.Password = value, isPassword: true);
			AddField(L.T("Confirm password"), () => viewModel.ConfirmPassword, value => viewModel.ConfirmPassword = value, isPassword: true);
			AddLink(L.T("Already have an account? Log in"), () => LoginRequested?.Invoke());
		}
	}
}
