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
	/// The logged-in member's own page: their name (shown to the band, changed here), their password,
	/// forgetting the password saved on this device (when one is), and logging out.
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
			var change = new Button { Content = L.T("Change password"), IsEnabled = false };
			var message = Message();

			void Update(object? sender, PropertyChangedEventArgs e)
			{
				change.IsEnabled = model.CanSubmit;
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
					current.Text = next.Text = confirm.Text = "";
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
					Explanation(L.T("Enter your current password and choose a new one. Changing it needs a connection to the server.")),
					current,
					next,
					confirm,
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
