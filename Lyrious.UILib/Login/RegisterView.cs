using System;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Login
{
	/// <summary>Creates a member account; the member is logged in afterwards.</summary>
	public sealed class RegisterView : FormPage
	{
		/// <summary>Raised when the user wants to go back to logging in.</summary>
		public event Action? LoginRequested;

		/// <summary>What has been typed in (kept when the form is made anew in another language).</summary>
		public RegisterViewModel ViewModel { get; }

		public RegisterView(RegisterViewModel viewModel)
			: base(viewModel, L.T("Create account"), L.T("Join Lyrious to share songs and setlists with your band."), L.T("Create account"), viewModel.RegisterAsync)
		{
			ViewModel = viewModel;
			AddField(L.T("Username"), () => viewModel.UserName, value => viewModel.UserName = value);
			AddField(L.T("Your name"), () => viewModel.DisplayName, value => viewModel.DisplayName = value);
			// An email address, or a phone number (only digits, spaces, + and -) to get the code by SMS.
			AddField(L.T("Email or phone number"), () => viewModel.Email, value => viewModel.Email = value);
			AddField(L.T("Password"), () => viewModel.Password, value => viewModel.Password = value, isPassword: true);
			AddField(L.T("Confirm password"), () => viewModel.ConfirmPassword, value => viewModel.ConfirmPassword = value, isPassword: true);
			// The device's language to start with (or the one chosen in Settings): the new account's. The
			// app follows it at once, so the form is shown anew in it (see MainView), with what was typed.
			AddControl(Setlists.PageLayout.LanguageChoice(viewModel.Language, code =>
			{
				viewModel.Language = code;
				Localization.Language.Chosen = code;
			}));
			// The second step: the code emailed to the address (or texted to the number).
			var code = AddField(L.T("Code from the email"), () => viewModel.Code, value => viewModel.Code = value);
			var resend = AddLink(L.T("Send a new code"), () => _ = viewModel.SendNewCodeAsync());
			AddLink(L.T("Already have an account? Log in"), () => LoginRequested?.Invoke());

			void ShowStep()
			{
				code.IsVisible = resend.IsVisible = viewModel.CodeSent;
				code.PlaceholderText = viewModel.IsPhone ? L.T("Code from the SMS") : L.T("Code from the email");
				SetSubmitText(viewModel.CodeSent ? L.T("Create account") : L.T("Send code"));
			}

			viewModel.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(RegisterViewModel.CodeSent))
				{
					ShowStep();
					if (viewModel.CodeSent)
					{
						code.Focus();
					}
				}
			};
			ShowStep();
		}
	}
}
