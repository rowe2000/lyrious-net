using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Login
{
	/// <summary>
	/// Asks, right after logging in, whether to save the password on this device. Used where the
	/// platform's password store doesn't ask the user itself.
	/// </summary>
	public sealed class SavePasswordView : UserControl
	{
		private readonly Button saveButton;

		/// <summary>Raised with true to save the password, false for "Not now".</summary>
		public event Action<bool>? Answered;

		public SavePasswordView(string userName)
		{
			saveButton = new Button
			{
				Content = "Save password",
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				IsDefault = true,
			};
			saveButton.Classes.Add("accent");
			saveButton.Click += (_, _) => Answered?.Invoke(true);

			var notNowButton = new Button
			{
				Content = "Not now",
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				IsCancel = true,
			};
			notNowButton.Click += (_, _) => Answered?.Invoke(false);

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
							Text = "Save password?",
							FontSize = 32,
							FontWeight = FontWeight.Bold,
							TextWrapping = TextWrapping.Wrap,
							TextAlignment = TextAlignment.Center,
							HorizontalAlignment = HorizontalAlignment.Center,
						},
						new TextBlock
						{
							Text = $"Save the password for {userName} on this device? Next time you log in, it's filled in for you.",
							TextWrapping = TextWrapping.Wrap,
							TextAlignment = TextAlignment.Center,
							Opacity = 0.7,
							Margin = new Thickness(0, 0, 0, 16),
						},
						saveButton,
						notNowButton,
					},
				},
			};
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));

			Content = new ScrollViewer { Content = card };
		}

		protected override void OnLoaded(RoutedEventArgs e)
		{
			base.OnLoaded(e);
			saveButton.Focus();
		}
	}
}
