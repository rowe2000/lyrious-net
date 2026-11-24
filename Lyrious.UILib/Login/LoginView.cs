using System;
using Lyrious.UILib.Localization;

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
			: base(viewModel, "Lyrious", L.T("Songs, setlists and lyrics for your band, in sync."), L.T("Log in"), viewModel.LogInAsync)
		{
			var userName = AddField(L.T("Username"), () => viewModel.UserName, value => viewModel.UserName = value);
			var password = AddField(L.T("Password"), () => viewModel.Password, value => viewModel.Password = value, isPassword: true);
			// In a browser: real web fields over these, so its password manager offers to save the login
			// and fills it in (see ILoginForm).
			App.LoginForm?.Attach(
				this,
				userName,
				password,
				SubmitButton,
				() => (viewModel.UserName, viewModel.Password),
				(typedUserName, typedPassword) =>
				{
					viewModel.UserName = typedUserName;
					viewModel.Password = typedPassword;
				},
				() =>
				{
					if (viewModel.CanSubmit)
					{
						_ = viewModel.LogInAsync();
					}
				});
			AddLink(L.T("Forgot my password?"), () => ForgotPasswordRequested?.Invoke());
			AddLink(L.T("Create an account"), () => RegisterRequested?.Invoke());
		}
	}
}
