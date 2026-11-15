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


			// The interface size: a list of sizes, applied at once.
			var scale = new ComboBox { MinWidth = 100 };
			foreach (var choice in UiScale.Choices)
			{
				scale.Items.Add(new ComboBoxItem { Content = UiScale.Describe(choice), Tag = choice });
			}

			var current = UiScale.Value;
			var closest = 0;
			for (var i = 0; i < UiScale.Choices.Count; i++)
			{
				if (Math.Abs(UiScale.Choices[i] - current) < Math.Abs(UiScale.Choices[closest] - current))
				{
					closest = i;
				}
			}

			scale.SelectedIndex = closest;
			scale.SelectionChanged += (_, _) =>
			{
				if (scale.SelectedItem is ComboBoxItem { Tag: double chosen })
				{
					UiScale.Value = chosen;
				}
			};

			var form = new StackPanel
			{
				Spacing = 16,
				Children =
				{
					Section("Display"),
					Setting(
						"Interface size",
						"Makes the whole app larger or smaller: text, buttons, icons and spacing.",
						scale),
					Section("Playback"),
					Setting(
						"Next song when stopping at the end",
						"When the lyrics have scrolled all the way to the bottom, pressing stop also goes on to the next song in the setlist.",
						nextOnStop),
					Section("Colors"),
					ColorSetting(
						"Highlight",
						"The current song, setlist and band, the selected row's outline, and the main buttons.",
						() => ColorSettings.Highlight,
						color => ColorSettings.Highlight = color,
						"System accent"),
					ColorSetting(
						"Chords",
						"Chords above the lyrics, and the bar beside a chorus.",
						() => ColorSettings.Chord,
						color => ColorSettings.Chord = color,
						"Theme blue"),
				},
			};

			// Scrolls when the settings don't fit (a short window or a large interface size).
			form.Margin = new Thickness(0, 0, 0, 16);
			var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			Content = PageLayout.Page("Settings", scroller, subtitle: "Kept on this device", closable: true);
		}

		/// <summary>The colours offered: bright enough for a dark background, deep enough for a light one.</summary>
		private static readonly string[] Swatches =
		[
			"#2F80ED", "#0EA5E9", "#14B8A6", "#22C55E", "#84CC16", "#EAB308",
			"#F97316", "#EF4444", "#EC4899", "#A855F7", "#6366F1", "#8E8E93",
		];

		/// <summary>A section's title, with room above.</summary>
		private static TextBlock Section(string title) => new()
		{
			Text = title,
			FontSize = 20,
			FontWeight = FontWeight.SemiBold,
			Margin = new Thickness(0, 12, 0, 0),
		};

		/// <summary>
		/// A colour setting: the theme's own ("Default"), a row of colours to pick from, and a box for
		/// any other ("#RRGGBB"). The chosen one is outlined; a change shows at once.
		/// </summary>
		private static Control ColorSetting(string name, string explanation, Func<Color?> get, Action<Color?> set, string defaultName)
		{
			var outlines = new System.Collections.Generic.List<(Color? Color, Border Outline)>();
			var custom = new TextBox { Width = 110, PlaceholderText = "#RRGGBB", VerticalAlignment = VerticalAlignment.Center };
			var problem = new TextBlock { Text = "Not a colour; write it as #RRGGBB.", IsVisible = false, TextWrapping = TextWrapping.Wrap };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));

			void ShowChosen()
			{
				var chosen = get();
				foreach (var (color, outline) in outlines)
				{
					if (color == chosen)
					{
						outline.Bind(Border.BorderBrushProperty, outline.GetResourceObservable(LyriousTheme.ForegroundBrush));
					}
					else
					{
						outline.BorderBrush = Brushes.Transparent;
					}
				}

				custom.Text = chosen is { } value ? ColorSettings.ToHex(value) : "";
				problem.IsVisible = false;
			}

			void Choose(Color? color)
			{
				set(color);
				ShowChosen();
			}

			Border Choice(Color? color, Control content, string tip)
			{
				var button = new Button
				{
					Content = content,
					Padding = new Thickness(0),
					MinWidth = 0,
					MinHeight = 0,
					HorizontalContentAlignment = HorizontalAlignment.Center,
					VerticalContentAlignment = VerticalAlignment.Center,
				};
				ToolTip.SetTip(button, tip);
				button.Click += (_, _) => Choose(color);
				var outline = new Border
				{
					BorderThickness = new Thickness(2),
					BorderBrush = Brushes.Transparent,
					CornerRadius = new CornerRadius(8),
					Padding = new Thickness(2),
					Child = button,
				};
				outlines.Add((color, outline));
				return outline;
			}

			var choices = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };
			choices.Children.Add(Choice(null, new TextBlock { Text = "Default", Margin = new Thickness(10, 0) , VerticalAlignment = VerticalAlignment.Center}, defaultName));
			((Button)outlines[0].Outline.Child!).Height = 32;
			foreach (var hex in Swatches)
			{
				var color = Color.Parse(hex);
				choices.Children.Add(Choice(
					color,
					new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(color) },
					hex));
			}

			void UseCustom()
			{
				var text = custom.Text?.Trim();
				if (string.IsNullOrEmpty(text))
				{
					Choose(null);
				}
				else if (ColorSettings.TryParse(text) is { } color)
				{
					Choose(color);
				}
				else
				{
					problem.IsVisible = true;
				}
			}

			custom.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					UseCustom();
				}
			};
			custom.LostFocus += (_, _) => UseCustom();
			choices.Children.Add(new Border { Padding = new Thickness(6, 2, 0, 2), Child = custom });

			ShowChosen();
			return new StackPanel
			{
				Spacing = 6,
				Children =
				{
					new TextBlock { Text = name, FontSize = 16, TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = explanation, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
					choices,
					problem,
				},
			};
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

		/// <summary>
		/// Band sync (the globe in the play toolbar): follow the joined band's current song, setlist
		/// and play/stop, and send this member's. On unless turned off.
		/// </summary>
		public static bool BandSync
		{
			get => App.Settings.Get(SettingKeys.BandSync) != "false";
			set => App.Settings.Set(SettingKeys.BandSync, value ? "true" : "false");
		}
	}
}
