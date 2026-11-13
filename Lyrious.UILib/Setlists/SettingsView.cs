using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The app's settings, kept on this device (<see cref="App.Settings"/>). Changes apply at once.</summary>
	public sealed class SettingsView : UserControl
	{
		public SettingsView()
		{
			var nextOnStop = new ToggleSwitch
			{
				IsChecked = PlaybackSettings.NextSongOnStopAtEnd,
				OnContent = "On",
				OffContent = "Off",
			};
			nextOnStop.IsCheckedChanged += (_, _) => PlaybackSettings.NextSongOnStopAtEnd = nextOnStop.IsChecked == true;


			var form = new StackPanel
			{
				Spacing = 16,
				Children =
				{
					Setting(
						"Next song when stopping at the end",
						"When the lyrics have scrolled all the way to the bottom, pressing stop also goes on to the next song in the setlist.",
						nextOnStop),
				},
			};

			Content = PageLayout.Page("Settings", form, subtitle: "Kept on this device", closable: true);
		}

		/// <summary>One setting: its name and an explanation, with its control at the right.</summary>
		private static Control Setting(string name, string explanation, Control control)
		{
			control.VerticalAlignment = VerticalAlignment.Center;
			control.Margin = new Thickness(16, 0, 0, 0);
			DockPanel.SetDock(control, Dock.Right);
			return new DockPanel
			{
				Children =
				{
					control,
					new StackPanel
					{
						Spacing = 4,
						Children =
						{
							new TextBlock { Text = name, FontSize = 16, TextWrapping = TextWrapping.Wrap },
							new TextBlock { Text = explanation, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
						},
					},
				},
			};
		}
	}

	/// <summary>Playback settings, read from and saved to <see cref="App.Settings"/>.</summary>
	public static class PlaybackSettings
	{
		/// <summary>Stopping with the lyrics scrolled to the bottom goes on to the next song. On unless turned off.</summary>
		public static bool NextSongOnStopAtEnd
		{
			get => App.Settings.Get(SettingKeys.NextSongOnStopAtEnd) != "false";
			set => App.Settings.Set(SettingKeys.NextSongOnStopAtEnd, value ? "true" : "false");
		}
	}
}
