using System;

namespace Lyrious.UILib.Login
{
	/// <summary>Creates a member account; the member is logged in afterwards.</summary>
	public sealed class RegisterView : FormPage
	{
		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		public RegisterView(RegisterViewModel viewModel)
			: base(viewModel, "Create account", "Join Lyrious to share songs and setlists with your band.", "Create account", viewModel.RegisterAsync)
		{
			AddField("Username", () => viewModel.UserName, value => viewModel.UserName = value);
			AddField("Your name", () => viewModel.DisplayName, value => viewModel.DisplayName = value);
			AddField("Email (optional)", () => viewModel.Email, value => viewModel.Email = value);
			AddField("Password", () => viewModel.Password, value => viewModel.Password = value, isPassword: true);
			AddField("Confirm password", () => viewModel.ConfirmPassword, value => viewModel.ConfirmPassword = value, isPassword: true);
			AddLink("Already have an account? Log in", () => LoginRequested?.Invoke());
		}
	}
}
