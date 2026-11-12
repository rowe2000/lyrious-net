using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// Layout shared by the account pages: a centered card with a title, the page's fields, error
	/// and success messages, a busy bar, a submit button and optional links. Code-only; the page's
	/// state lives in a <see cref="FormViewModel"/> and is mirrored into the controls.
	/// </summary>
	public abstract class FormPage : UserControl
	{
		private readonly FormViewModel viewModel;
		private readonly StackPanel fields = new() { Spacing = 12 };
		private readonly StackPanel links = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
		private readonly TextBlock errorText;
		private readonly TextBlock successText;
		private readonly ProgressBar busyBar;
		private readonly Button submitButton;
		private readonly List<(TextBox Box, Func<string> Get)> boundFields = [];

		protected FormPage(FormViewModel viewModel, string title, string subtitle, string submitText, Func<Task> submit)
		{
			this.viewModel = viewModel;
			DataContext = viewModel;

			errorText = Message(LyriousTheme.ErrorBrush);
			successText = Message(LyriousTheme.SuccessBrush);
			busyBar = new ProgressBar { IsIndeterminate = true, IsVisible = false };

			submitButton = new Button
			{
				Content = submitText,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				// Enter anywhere on the page submits.
				IsDefault = true,
			};
			submitButton.Classes.Add("accent");
			submitButton.Click += async (_, _) => await submit();

			var card = new Border
			{
				MaxWidth = 360,
				Margin = new Thickness(16),
				Padding = new Thickness(32),
				CornerRadius = new CornerRadius(8),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Child = new StackPanel
				{
					Spacing = 12,
					Children =
					{
						new TextBlock
						{
							Text = title,
							FontSize = 32,
							FontWeight = FontWeight.Bold,
							HorizontalAlignment = HorizontalAlignment.Center,
							// Long titles wrap instead of being cut off by the card.
							TextWrapping = TextWrapping.Wrap,
							TextAlignment = TextAlignment.Center,
						},
						new TextBlock
						{
							Text = subtitle,
							TextWrapping = TextWrapping.Wrap,
							TextAlignment = TextAlignment.Center,
							Opacity = 0.7,
							Margin = new Thickness(0, 0, 0, 16),
						},
						fields,
						errorText,
						successText,
						busyBar,
						submitButton,
						links,
					},
				},
			};
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));

			// Scrollable, so the form stays reachable on small screens with the on-screen keyboard open.
			Content = new ScrollViewer { Content = card };

			viewModel.PropertyChanged += (_, _) => Sync();
		}

		/// <summary>Adds a text field bound to a view-model property.</summary>
		protected TextBox AddField(string placeholder, Func<string> get, Action<string> set, bool isPassword = false)
		{
			var box = new TextBox { PlaceholderText = placeholder, Text = get() };
			if (isPassword)
			{
				box.PasswordChar = '•';
				box.Classes.Add("revealPasswordButton");
			}

			box.TextChanged += (_, _) => set(box.Text ?? "");
			fields.Children.Add(box);
			boundFields.Add((box, get));
			Sync();
			return box;
		}

		/// <summary>Adds a link-style button below the submit button, e.g. to another page.</summary>
		protected void AddLink(string text, Action onClick)
		{
			var link = new HyperlinkButton { Content = text, HorizontalAlignment = HorizontalAlignment.Center };
			link.Click += (_, _) => onClick();
			links.Children.Add(link);
		}

		protected override void OnLoaded(RoutedEventArgs e)
		{
			base.OnLoaded(e);
			if (boundFields.Count > 0)
			{
				boundFields[0].Box.Focus();
			}
		}

		private void Sync()
		{
			submitButton.IsEnabled = viewModel.CanSubmit;
			busyBar.IsVisible = viewModel.IsBusy;
			ShowMessage(errorText, viewModel.ErrorMessage);
			ShowMessage(successText, viewModel.SuccessMessage);

			foreach (var (box, get) in boundFields)
			{
				box.IsEnabled = !viewModel.IsBusy;
				// The view model clears fields itself, e.g. passwords after submitting.
				if (box.Text != get())
				{
					box.Text = get();
				}
			}
		}

		private static TextBlock Message(string brushKey)
		{
			var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(brushKey));
			return text;
		}

		private static void ShowMessage(TextBlock text, string? message)
		{
			text.Text = message;
			text.IsVisible = !string.IsNullOrEmpty(message);
		}
	}
}
