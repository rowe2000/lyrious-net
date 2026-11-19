using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// The first page of onboarding: sign in, or go on to create an account. It also shows which
	/// server (host) the app talks to, with a link to change it: a wrong or unreachable host can
	/// always be fixed from here, without logging in first.
	/// </summary>
	public sealed class LoginView : FormPage
	{
		/// <summary>Raised when the user wants to create an account instead.</summary>
		public event Action? RegisterRequested;

		/// <summary>Raised when the user has forgotten their password.</summary>
		public event Action? ForgotPasswordRequested;

		/// <summary>Raised when the user wants to change the server (host).</summary>
		public event Action? HostRequested;

		/// <param name="host">The server the app talks to; shown with a "change" link when given.</param>
		public LoginView(LoginViewModel viewModel, string? host = null)
			: base(viewModel, "Lyrious", L.T("Songs, setlists and lyrics for your band, in sync."), L.T("Log in"), viewModel.LogInAsync)
		{
			AddField(L.T("Username"), () => viewModel.UserName, value => viewModel.UserName = value);
			AddField(L.T("Password"), () => viewModel.Password, value => viewModel.Password = value, isPassword: true);
			AddLink(L.T("Forgot my password?"), () => ForgotPasswordRequested?.Invoke());
			AddLink(L.T("New to Lyrious? Create an account"), () => RegisterRequested?.Invoke());
			if (!string.IsNullOrEmpty(host))
			{
				AddLink(L.T("Host: {0} · Change", host), () => HostRequested?.Invoke());
			}
		}
	}
}
