using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Lyrious.UILib.Login
{
	/// <summary>State and behaviour of the login page, independent of how it's drawn.</summary>
	public sealed class LoginViewModel(IAuthService authService) : INotifyPropertyChanged
	{
		private string userName = "";
		private string password = "";
		private bool isBusy;
		private string? errorMessage;

		public event PropertyChangedEventHandler? PropertyChanged;

		/// <summary>Raised after a successful login.</summary>
		public event Action<AuthenticatedUser>? LoggedIn;

		public string UserName
		{
			get => userName;
			set => Set(ref userName, value, alsoNotify: nameof(CanLogIn));
		}

		public string Password
		{
			get => password;
			set => Set(ref password, value, alsoNotify: nameof(CanLogIn));
		}

		public bool IsBusy
		{
			get => isBusy;
			private set => Set(ref isBusy, value, alsoNotify: nameof(CanLogIn));
		}

		public string? ErrorMessage
		{
			get => errorMessage;
			private set => Set(ref errorMessage, value);
		}

		public bool CanLogIn => !IsBusy && !string.IsNullOrWhiteSpace(UserName) && Password.Length > 0;

		public async Task LogInAsync()
		{
			if (!CanLogIn)
			{
				return;
			}

			IsBusy = true;
			ErrorMessage = null;
			try
			{
				var result = await authService.LoginAsync(UserName.Trim(), Password);
				if (result.User is { } user)
				{
					Password = "";
					LoggedIn?.Invoke(user);
				}
				else
				{
					ErrorMessage = result.Error ?? "Login failed.";
				}
			}
			catch (Exception ex)
			{
				ErrorMessage = $"Couldn't reach the server. {ex.Message}";
			}
			finally
			{
				IsBusy = false;
			}
		}

		private void Set<T>(ref T field, T value, string? alsoNotify = null, [CallerMemberName] string? propertyName = null)
		{
			if (Equals(field, value))
			{
				return;
			}

			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			if (alsoNotify != null)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(alsoNotify));
			}
		}
	}
}
