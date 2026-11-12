using System;

namespace Lyrious.UILib.Login
{
	/// <summary>The first page of onboarding: sign in, or go on to create an account.</summary>
	public sealed class LoginView : FormPage
	{
		/// <summary>Raised when the user wants to create an account instead.</summary>
		public event Action? RegisterRequested;

		/// <summary>Raised when the user has forgotten their password.</summary>
		public event Action? ForgotPasswordRequested;

		public LoginView(LoginViewModel viewModel)
			: base(viewModel, "Lyrious", "Songs, setlists and lyrics for your band, in sync.", "Log in", viewModel.LogInAsync)
		{
			AddField("Username", () => viewModel.UserName, value => viewModel.UserName = value);
			AddField("Password", () => viewModel.Password, value => viewModel.Password = value, isPassword: true);
			AddLink("Forgot my password?", () => ForgotPasswordRequested?.Invoke());
			AddLink("New to Lyrious? Create an account", () => RegisterRequested?.Invoke());
		}
	}
}
