using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Login;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The logged-in member's own page: their name (shown to the band, changed here), their email
	/// address and their password (each changed with a code emailed to them), forgetting the password
	/// saved on this device (when one is), and logging out.
	/// </summary>
	public sealed class ProfileView : UserControl
	{
		/// <summary>Raised with the new name once it's saved.</summary>
		public event Action<string>? Renamed;

		/// <summary>Raised with the new password once it's changed (e.g. to update a saved one).</summary>
		public event Action<string>? PasswordChanged;

		/// <summary>Raised when "Forget saved password" is pressed.</summary>
		public event Action? ForgetSavedPasswordRequested;

		/// <summary>Raised when "Log out" is pressed.</summary>
		public event Action? LogOutRequested;

		/// <param name="offerForgetSavedPassword">Shows "Forget saved password" (a password is saved here and the app can remove it).</param>
		public ProfileView(AuthenticatedUser user, ISetlistService service, IAuthService auth, bool offerForgetSavedPassword)
		{
			var form = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					NameSection(user, service),
					EmailSection(auth),
					PasswordSection(auth),
				},
			};

			if (offerForgetSavedPassword)
			{
				form.Children.Add(Section(L.T("Saved password")));
				var forget = new Button { Content = L.T("Forget saved password") };
				var forgotten = new TextBlock { Text = L.T("The password is no longer saved on this device."), Opacity = 0.7, IsVisible = false, TextWrapping = TextWrapping.Wrap };
				forget.Click += (_, _) =>
				{
					// Gone once used: there's nothing saved any more.
					forget.IsVisible = false;
					forgotten.IsVisible = true;
					ForgetSavedPasswordRequested?.Invoke();
				};
				form.Children.Add(Explanation(L.T("This device fills in your password when you log in.")));
				form.Children.Add(forget);
				form.Children.Add(forgotten);
			}

			form.Children.Add(Section(L.T("Log out")));
			form.Children.Add(Explanation(L.T("Logged in as {0} on {1}.", user.UserName, auth.ServerAddress)));
			var logOut = new Button { Content = L.T("Log out") };
			logOut.Click += (_, _) => LogOutRequested?.Invoke();
			form.Children.Add(logOut);

			form.Margin = new Thickness(0, 0, 0, 16);
			var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			Content = PageLayout.Page(L.T("Profile"), scroller, subtitle: user.UserName, closable: true, icon: AppIcons.Profile);
		}

		private Control NameSection(AuthenticatedUser user, ISetlistService service)
		{
			var name = new TextBox { Text = user.DisplayName, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
			var save = new Button { Content = L.T("Save name"), IsEnabled = false };
			var message = Message();
			var saved = user.DisplayName;

			name.TextChanged += (_, _) =>
			{
				save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && name.Text.Trim() != saved;
				message.IsVisible = false;
			};

			async Task SaveAsync()
			{
				var newName = name.Text?.Trim() ?? "";
				if (newName.Length == 0 || newName == saved)
				{
					return;
				}

				save.IsEnabled = false;
				try
				{
					await service.RenameMeAsync(newName);
					saved = newName;
					Show(message, L.T("Your name has been changed."), error: false);
					Renamed?.Invoke(newName);
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't change your name. {0}", ex.Message), error: true);
					save.IsEnabled = true;
				}
			}

			save.Click += (_, _) => _ = SaveAsync();
			name.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter && save.IsEnabled)
				{
					e.Handled = true;
					_ = SaveAsync();
				}
			};

			return new StackPanel
			{
				Spacing = 8,
				Children =
				{
					Section(L.T("Name")),
					Explanation(L.T("How the band sees you, in the members list and the playback toolbar.")),
					name,
					save,
					message,
				},
			};
		}

		private Control PasswordSection(IAuthService auth)
		{
			var model = new ChangePasswordViewModel(auth);
			model.PasswordChanged += newPassword => PasswordChanged?.Invoke(newPassword);

			TextBox PasswordBox(string placeholder, Action<string> set)
			{
				var box = new TextBox { PasswordChar = '•', PlaceholderText = placeholder, MaxWidth = 360, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
				box.TextChanged += (_, _) => set(box.Text ?? "");
				return box;
			}

			var current = PasswordBox(L.T("Current password"), value => model.CurrentPassword = value);
			var next = PasswordBox(L.T("New password"), value => model.NewPassword = value);
			var confirm = PasswordBox(L.T("Confirm new password"), value => model.ConfirmPassword = value);
			// The code emailed to the member, beside the button that sends it.
			var code = new TextBox { PlaceholderText = L.T("Code from the email"), MinWidth = 160, MaxWidth = 200 };
			code.TextChanged += (_, _) => model.Code = code.Text ?? "";
			var sendCode = new Button { Content = L.T("Send code"), VerticalAlignment = VerticalAlignment.Center };
			sendCode.Click += (_, _) => _ = model.SendCodeAsync();
			var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { code, sendCode } };
			var change = new Button { Content = L.T("Change password"), IsEnabled = false };
			var message = Message();

			void Update(object? sender, PropertyChangedEventArgs e)
			{
				change.IsEnabled = model.CanSubmit;
				sendCode.IsEnabled = !model.IsBusy;
				if (model.ErrorMessage is { } error)
				{
					Show(message, error, error: true);
				}
				else if (model.SuccessMessage is { } success)
				{
					Show(message, success, error: false);
				}
				else
				{
					message.IsVisible = false;
				}

				// Changed: the boxes are emptied (as the model's own fields are).
				if (e.PropertyName == nameof(FormViewModel.SuccessMessage) && model.SuccessMessage != null)
				{
					current.Text = next.Text = confirm.Text = code.Text = "";
				}
			}

			model.PropertyChanged += Update;
			change.Click += (_, _) => _ = model.ChangePasswordAsync();
			confirm.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter && model.CanSubmit)
				{
					e.Handled = true;
					_ = model.ChangePasswordAsync();
				}
			};

			return new StackPanel
			{
				Spacing = 8,
				Children =
				{
					Section(L.T("Password")),
					Explanation(L.T("Enter your current password, choose a new one, and enter the code that \"Send code\" emails to you. Changing it needs a connection to the server.")),
					current,
					next,
					confirm,
					codeRow,
					change,
					message,
				},
			};
		}

		/// <summary>
		/// The member's email address, and changing it: a code is emailed to the new address, and the
		/// change is made with it, so the address is known to be theirs.
		/// </summary>
		private Control EmailSection(IAuthService auth)
		{
			var shown = new TextBlock { TextWrapping = TextWrapping.Wrap };
			void ShowAddress() => shown.Text = auth.Email is { } email
				? L.T("Your email address is {0}.", email)
				: L.T("Your account has no email address. Add one to reset a forgotten password and to change your password.");
			ShowAddress();

			var address = new TextBox { PlaceholderText = L.T("New email address"), MaxWidth = 360, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
			var sendCode = new Button { Content = L.T("Send code"), IsEnabled = false };
			var code = new TextBox { PlaceholderText = L.T("Code from the email"), MaxWidth = 200, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
			var change = new Button { Content = L.T("Change email"), IsVisible = false, IsEnabled = false };
			var message = Message();
			string? sentTo = null;
			var busy = false;

			void Enable()
			{
				var typed = address.Text?.Trim() ?? "";
				sendCode.IsEnabled = !busy && typed.Length > 0;
				// The code is for the address it was sent to: another address needs a new one.
				var codeStep = sentTo != null && typed == sentTo;
				code.IsVisible = change.IsVisible = codeStep;
				change.IsEnabled = !busy && codeStep && (code.Text?.Trim().Length ?? 0) > 0;
			}

			async Task RunAsync(Func<Task> action)
			{
				busy = true;
				Enable();
				try
				{
					await action();
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't reach the server. {0}", ex.Message), error: true);
				}
				finally
				{
					busy = false;
					Enable();
				}
			}

			sendCode.Click += (_, _) => _ = RunAsync(async () =>
			{
				var newAddress = address.Text?.Trim() ?? "";
				var result = await auth.SendEmailChangeCodeAsync(newAddress);
				if (result.Succeeded)
				{
					sentTo = newAddress;
					Show(message, L.T("A code has been sent to {0}. Enter it to make it your email address.", newAddress), error: false);
					code.Focus();
				}
				else
				{
					Show(message, result.Error ?? L.T("Couldn't send a code."), error: true);
				}
			});
			change.Click += (_, _) => _ = RunAsync(async () =>
			{
				var result = await auth.ChangeEmailAsync(sentTo!, code.Text?.Trim() ?? "");
				if (result.Succeeded)
				{
					sentTo = null;
					address.Text = code.Text = "";
					ShowAddress();
					Show(message, L.T("Your email address has been changed."), error: false);
				}
				else
				{
					Show(message, result.Error ?? L.T("Changing the email address failed."), error: true);
				}
			});
			address.TextChanged += (_, _) => Enable();
			code.TextChanged += (_, _) => Enable();

			return new StackPanel
			{
				Spacing = 8,
				Children =
				{
					Section(L.T("Email")),
					shown,
					Explanation(L.T("To change it, enter the new address and press \"Send code\"; then enter the code from the email sent there.")),
					address,
					sendCode,
					code,
					change,
					message,
				},
			};
		}

		private static TextBlock Section(string title) => new()
		{
			Text = title,
			FontSize = 20,
			FontWeight = FontWeight.SemiBold,
			Margin = new Thickness(0, 12, 0, 0),
		};

		private static TextBlock Explanation(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

		private static TextBlock Message() => new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };

		private static void Show(TextBlock message, string text, bool error)
		{
			message.Text = text;
			message.IsVisible = true;
			if (error)
			{
				message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(LyriousTheme.ErrorBrush));
			}
			else
			{
				message.ClearValue(TextBlock.ForegroundProperty);
			}
		}
	}
}
