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

		/// <summary>Raised when the header's Log out tool is pressed.</summary>
		public event Action? LogOutRequested;

		/// <summary>Raised when "Forget saved password" is pressed.</summary>
		public event Action? ForgetSavedPasswordRequested;


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
				form.Children.Add(forget);
				form.Children.Add(forgotten);
			}

			form.Margin = new Thickness(0, 0, 0, 16);
			var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			// The header's tools: logging in as site manager (for those who may), and logging out.
			var logOut = HomeView.IconButton(AppIcons.LogOut, L.T("Log out"));
			logOut.Click += (_, _) => LogOutRequested?.Invoke();
			var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			if (ElevateTool(auth) is { } elevate)
			{
				tools.Children.Add(elevate);
			}

			tools.Children.Add(logOut);
			Content = PageLayout.Page(L.T("Profile"), scroller, subtitle: user.UserName, titleAction: tools, closable: true, icon: AppIcons.Profile);
		}

		private Control NameSection(AuthenticatedUser user, ISetlistService service)
		{
			var name = new TextBox { Text = user.DisplayName, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240, VerticalAlignment = VerticalAlignment.Center };
			var message = Message();
			var saved = user.DisplayName;
			// A ✓ in the box ("Save name" as its tip), shown once the name differs from the saved one.
			var save = PageLayout.OkButton(L.T("Save name"));
			PageLayout.OkInBox(name, save, () => !string.IsNullOrWhiteSpace(name.Text) && name.Text.Trim() != saved);

			name.TextChanged += (_, _) => message.IsVisible = false;

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
					// Saved: nothing is changed any more, so no ✓.
					save.IsVisible = false;
					save.IsEnabled = true;
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
				if (e.Key == Avalonia.Input.Key.Enter && save.IsVisible && save.IsEnabled)
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
					name,
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
		/// The member's email address, in a box to change it: once it differs from theirs, a tick sends a
		/// code to the new address; then the code is entered and verified, which makes the change, so the
		/// address is known to be theirs.
		/// </summary>
		private Control EmailSection(IAuthService auth)
		{
			var address = new TextBox { Text = auth.Email ?? "", PlaceholderText = L.T("Email address"), MaxWidth = 360, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
			// A ✓ in the box ("Send code" as its tip), shown once the address differs from theirs.
			var sendCode = PageLayout.OkButton(L.T("Send code"));
			var sent = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			var code = new TextBox { PlaceholderText = L.T("Code from the email"), MaxWidth = 200, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
			var verify = new Button { Content = L.T("Verify"), IsVisible = false, IsEnabled = false };
			var message = Message();
			string? sentTo = null;
			var busy = false;

			bool Changed()
			{
				var typed = address.Text?.Trim() ?? "";
				return typed.Length > 0 && !string.Equals(typed, auth.Email, StringComparison.OrdinalIgnoreCase);
			}

			PageLayout.OkInBox(address, sendCode, Changed);

			void Enable()
			{
				var typed = address.Text?.Trim() ?? "";
				sendCode.IsVisible = Changed();
				sendCode.IsEnabled = !busy;
				// The code is for the address it was sent to: another address needs a new one.
				var codeStep = sentTo != null && typed == sentTo;
				sent.IsVisible = code.IsVisible = verify.IsVisible = codeStep;
				verify.IsEnabled = !busy && codeStep && (code.Text?.Trim().Length ?? 0) > 0;
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
				message.IsVisible = false;
				var result = await auth.SendEmailChangeCodeAsync(newAddress);
				if (result.Succeeded)
				{
					sentTo = newAddress;
					sent.Text = L.T("A code was sent to {0}. Please verify it.", newAddress);
					code.Text = "";
					code.Focus();
				}
				else
				{
					Show(message, result.Error ?? L.T("Couldn't send a code."), error: true);
				}
			});
			verify.Click += (_, _) => _ = RunAsync(async () =>
			{
				var result = await auth.ChangeEmailAsync(sentTo!, code.Text?.Trim() ?? "");
				if (result.Succeeded)
				{
					// The box keeps the new address, now theirs (so the tick goes).
					sentTo = null;
					code.Text = "";
					Show(message, L.T("Your email address has been changed."), error: false);
				}
				else
				{
					Show(message, result.Error ?? L.T("Changing the email address failed."), error: true);
				}
			});
			address.TextChanged += (_, _) =>
			{
				// A new address: what was said about the last one (e.g. an error) no longer applies.
				message.IsVisible = false;
				Enable();
			};
			code.TextChanged += (_, _) =>
			{
				// Typing the code again: the last error (e.g. a wrong code) no longer applies.
				message.IsVisible = false;
				Enable();
			};
			code.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter && verify.IsEnabled)
				{
					e.Handled = true;
					verify.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
				}
			};

			return new StackPanel
			{
				Spacing = 8,
				Children =
				{
					Section(L.T("Email")),
					address,
					sent,
					code,
					verify,
					message,
				},
			};
		}

		/// <summary>
		/// For a member the login said may elevate (CanElevate: listed as a site manager), until they
		/// have: a tool in the header that opens a popup to log in as site manager (their password
		/// again). Null for other members, and once elevated.
		/// </summary>
		private static Button? ElevateTool(IAuthService auth)
		{
			// Only for a member the login said may elevate (listed as a site manager), until they have.
			if (!auth.CanElevate || auth.IsSiteManager)
			{
				return null;
			}

			var tool = HomeView.IconButton(AppIcons.SiteManager, L.T("Log in as site manager"));
			var popup = ElevateFlyout(auth, () => tool.IsVisible = false);
			tool.Click += (_, _) => popup.ShowAt(tool);
			return tool;
		}

		/// <summary>
		/// The popup to log in as site manager: the member's password again, and Log in (or Enter);
		/// what went wrong shows in it. It closes once logged in elevated, and then calls <paramref name="elevated"/>.
		/// </summary>
		internal static Flyout ElevateFlyout(IAuthService auth, Action? elevated = null)
		{
			var password = new TextBox { PasswordChar = '•', PlaceholderText = L.T("Password"), MinWidth = 220, Classes = { "revealPasswordButton" } };
			var logIn = new Button { Content = L.T("Log in"), Classes = { "accent" }, IsEnabled = false };
			var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260, IsVisible = false };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));
			var popup = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 8,
					Children =
					{
						new TextBlock { Text = L.T("Log in as site manager"), FontWeight = FontWeight.SemiBold },
						new TextBlock { Text = L.T("Your password again, for the rights to manage every band and member."), TextWrapping = TextWrapping.Wrap, MaxWidth = 260, Opacity = 0.75 },
						password,
						logIn,
						problem,
					},
				},
			};

			password.TextChanged += (_, _) => logIn.IsEnabled = !string.IsNullOrEmpty(password.Text);
			async Task LogInAsync()
			{
				logIn.IsEnabled = false;
				problem.IsVisible = false;
				try
				{
					var result = await auth.ElevateAsync(password.Text ?? "");
					if (result.Succeeded)
					{
						password.Text = "";
						popup.Hide();
						elevated?.Invoke();
						return;
					}

					problem.Text = result.Error ?? L.T("Couldn't log in as site manager.");
				}
				catch (Exception ex)
				{
					problem.Text = L.T("Couldn't reach the server. {0}", ex.Message);
				}

				problem.IsVisible = true;
				logIn.IsEnabled = !string.IsNullOrEmpty(password.Text);
			}

			logIn.Click += (_, _) => _ = LogInAsync();
			password.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter && logIn.IsEnabled)
				{
					e.Handled = true;
					_ = LogInAsync();
				}
			};
			popup.Opened += (_, _) => password.Focus();
			return popup;
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
