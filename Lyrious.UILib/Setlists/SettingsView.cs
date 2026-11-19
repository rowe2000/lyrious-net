using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The app's settings, kept on this device (<see cref="App.Settings"/>). Changes apply at once.</summary>
	public sealed class SettingsView : UserControl
	{
		/// <param name="importSongbookPro">
		/// Picks a SongbookPro backup and imports it into the joined band (converted to Lyrious' format
		/// or not), returning what happened (null: nothing picked); throws
		/// <see cref="InvalidOperationException"/> with the reason when it can't.
		/// </param>
		/// <param name="clearSongbook">Deletes every song in the joined band's songbook, returning what happened.</param>
		/// <param name="bandName">The joined band's name, for the Songbook section. None (no band joined): no Songbook section.</param>
		/// <param name="bandSettings">The current band's settings (saved with the band, the same for all its members), shown under "Band settings".</param>
		public SettingsView(Func<Control, bool, Task<string?>>? importSongbookPro = null, Func<Task<string>>? clearSongbook = null, string? bandName = null, Control? bandSettings = null)
		{
			var nextOnStop = new ToggleSwitch
			{
				IsChecked = PlaybackSettings.NextSongOnStopAtEnd,
				OnContent = "On",
				OffContent = "Off",
			};
			nextOnStop.IsCheckedChanged += (_, _) => PlaybackSettings.NextSongOnStopAtEnd = nextOnStop.IsChecked == true;

			var songHeader = new ToggleSwitch
			{
				IsChecked = DisplaySettings.ShowSongHeader,
				OnContent = "On",
				OffContent = "Off",
			};
			songHeader.IsCheckedChanged += (_, _) => DisplaySettings.ShowSongHeader = songHeader.IsChecked == true;

			var dynamicZoom = new ToggleSwitch
			{
				IsChecked = DisplaySettings.DynamicZoom,
				OnContent = "On",
				OffContent = "Off",
			};
			dynamicZoom.IsCheckedChanged += (_, _) => DisplaySettings.DynamicZoom = dynamicZoom.IsChecked == true;

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
					Setting(
						"Song title above the lyrics",
						"Shows the song's name, key, tempo and beat above the lyrics. The song's name is in the drawer either way.",
						songHeader),
					Setting(
						"Dynamic zoom",
						"Sizes the whole app, lyrics included, to the window: the smaller of its width and height sets the zoom, where 500 px is 100 %. The interface size above and the lyrics' own zoom apply on top.",
						dynamicZoom),
					Section("Playback"),
					Setting(
						"Next song when stopping at the end",
						"When the lyrics have scrolled all the way to the bottom, pressing stop also goes on to the next song in the setlist.",
						nextOnStop),
					Section("Colors"),
					new TextBlock { Text = "Yours on all your devices.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
					ColorSetting(
						"Highlight",
						"The current song, setlist and band, the selected row's outline, and the main buttons.",
						() => ColorSettings.Highlight,
						color => ColorSettings.Highlight = color,
						"Theme purple",
						"SystemAccentColor"),
					ColorSetting(
						"Chords",
						"Chords above the lyrics, and the bar beside a chorus.",
						() => ColorSettings.Chord,
						color => ColorSettings.Chord = color,
						"Theme colour",
						LyriousTheme.ChordBrush),
					ColorSetting(
						"Heading 1",
						"The largest headings in the lyrics (\"# \"), such as the song's title.",
						() => ColorSettings.Heading1,
						color => ColorSettings.Heading1 = color,
						"Text colour",
						LyriousTheme.Heading1Brush),
					ColorSetting(
						"Heading 2",
						"Headings in the lyrics written \"## \", such as Verse and Chorus.",
						() => ColorSettings.Heading2,
						color => ColorSettings.Heading2 = color,
						"Theme colour",
						LyriousTheme.Heading2Brush),
					ColorSetting(
						"Other headings",
						"Smaller headings in the lyrics (\"### \" and more).",
						() => ColorSettings.Heading,
						color => ColorSettings.Heading = color,
						"Theme colour",
						LyriousTheme.HeadingBrush),
				},
			};

			// The joined band's songbook: importing into it, and clearing it.
			// The current band's settings: saved with the band, not on this device.
			if (bandName != null && bandSettings != null)
			{
				form.Children.Add(Section($"Band settings · {bandName}"));
				form.Children.Add(new TextBlock { Text = "Saved with the band: the same for all its members.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
				form.Children.Add(bandSettings);
			}

			if (bandName != null && (importSongbookPro != null || clearSongbook != null))
			{
				form.Children.Add(Section($"Songbook · {bandName}"));
				if (importSongbookPro != null)
				{
					form.Children.Add(ImportSetting(importSongbookPro));
				}

				if (clearSongbook != null)
				{
					form.Children.Add(ClearSetting(clearSongbook, bandName));
				}
			}

			// Scrolls when the settings don't fit (a short window or a large interface size).
			form.Margin = new Thickness(0, 0, 0, 16);
			var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			Content = PageLayout.Page("Settings", scroller, subtitle: "Kept on this device", closable: true);
		}

		/// <summary>
		/// "Import SongbookPro backup…": a button that first asks whether to convert the songs to
		/// Lyrious' format (Convert, Keep as is, or Cancel), then picks the backup and imports it; what
		/// happened (or why it couldn't) shows below it.
		/// </summary>
		private static Control ImportSetting(Func<Control, bool, Task<string?>> import)
		{
			var button = new Button { Content = "Import SongbookPro backup…" };
			var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Opacity = 0.85 };

			async Task ImportAsync(bool convert)
			{
				button.IsEnabled = false;
				status.IsVisible = true;
				status.Text = "Importing…";
				try
				{
					var result = await import(button, convert);
					status.Text = result ?? "";
					status.IsVisible = result != null;
				}
				catch (InvalidOperationException ex)
				{
					status.Text = ex.Message;
				}
				finally
				{
					button.IsEnabled = true;
				}
			}

			var convertButton = new Button { Content = "Convert", Classes = { "accent" } };
			var keepButton = new Button { Content = "Keep as is" };
			var cancelButton = new Button { Content = "Cancel" };
			var question = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 340,
					Children =
					{
						new TextBlock { Text = "Convert the songs to Lyrious format?", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
						new TextBlock
						{
							Text = "Each song then starts with a heading of its key and name (\"# [D]Amazing Grace\"), comments such as {c: Verse 1} become headings (\"## Verse 1\"), and a heading like \"# [d] Intro\" becomes \"# [D]Intro\". Songs by Maria, Tobbe or Robert get the name as a tag instead of as the artist, and a ✓ in a name or the lyrics becomes the tag ✓.",
							TextWrapping = TextWrapping.Wrap,
							Opacity = 0.8,
						},
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { convertButton, keepButton, cancelButton } },
					},
				},
			};
			convertButton.Click += (_, _) =>
			{
				question.Hide();
				_ = ImportAsync(convert: true);
			};
			keepButton.Click += (_, _) =>
			{
				question.Hide();
				_ = ImportAsync(convert: false);
			};
			cancelButton.Click += (_, _) => question.Hide();
			button.Flyout = question;

			return new StackPanel
			{
				Spacing = 8,
				Children =
				{
					new TextBlock
					{
						Text = "Adds the songs and sets of a SongbookPro backup (\"SongbookPro Backup.zip\" or .sbpbackup) to your band's songbook and setlists. PDF songs are left out.",
						TextWrapping = TextWrapping.Wrap,
						Opacity = 0.7,
					},
					button,
					status,
				},
			};
		}

		/// <summary>
		/// "Clear songbook…": a button that asks first (in a popup, with a red Delete), then deletes every
		/// song in the band's songbook; what happened shows below it.
		/// </summary>
		private static Control ClearSetting(Func<Task<string>> clear, string bandName)
		{
			var button = new Button { Content = "Clear songbook…" };
			var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Opacity = 0.85 };
			var confirm = PageLayout.DangerButton("Delete all songs");
			var cancel = new Button { Content = "Cancel" };
			var question = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 320,
					Children =
					{
						new TextBlock
						{
							Text = $"Delete every song in {bandName}'s songbook? They're removed from every setlist too (the setlists stay, empty). This can't be undone.",
							TextWrapping = TextWrapping.Wrap,
						},
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } },
					},
				},
			};
			cancel.Click += (_, _) => question.Hide();
			confirm.Click += async (_, _) =>
			{
				question.Hide();
				button.IsEnabled = false;
				status.IsVisible = true;
				status.Text = "Deleting…";
				try
				{
					status.Text = await clear();
				}
				catch (Exception ex)
				{
					status.Text = $"Couldn't clear the songbook. {ex.Message}";
				}
				finally
				{
					button.IsEnabled = true;
				}
			};
			button.Flyout = question;

			return new StackPanel
			{
				Spacing = 8,
				Margin = new Thickness(0, 8, 0, 0),
				Children =
				{
					new TextBlock { Text = "Deletes all the songs in the songbook, for everyone in the band.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
					button,
					status,
				},
			};
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
		/// A colour setting: a button showing the colour and its code (for the theme's own, the colour
		/// <paramref name="defaultResource"/> holds, with <paramref name="defaultName"/>) that opens a
		/// popup to pick another: hue, saturation and lightness (see <see cref="HslColorPicker"/>), a
		/// box for "#RRGGBB", a row of ready colours, and Default. A change shows at once.
		/// </summary>
		private static Control ColorSetting(string name, string explanation, Func<Color?> get, Action<Color?> set, string defaultName, string defaultResource)
		{
			var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Gray, 0.6) };
			var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
			var button = new Button
			{
				Padding = new Thickness(6),
				// Always as wide, whatever it says, so the popup under it stays put while a colour is picked.
				Width = 130,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Content = new DockPanel { Children = { swatch, label } },
			};

			var picker = new HslColorPicker();
			var preview = new Border { Width = 40, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Gray, 0.6) };
			var hex = new TextBox { Width = 110, PlaceholderText = "#RRGGBB", VerticalAlignment = VerticalAlignment.Center };
			var problem = new TextBlock { Text = "Not a colour; write it as #RRGGBB.", IsVisible = false, TextWrapping = TextWrapping.Wrap };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));

			// The theme's own colour for this setting, followed as the theme changes (null until known).
			Color? themeColor = null;

			// The button, preview and box show the setting (or the theme's colour); the picker shows it
			// too unless it's being dragged.
			void Show(bool updatePicker = true)
			{
				var chosen = get();
				var shown = chosen ?? themeColor;
				swatch.Background = shown is { } color ? new SolidColorBrush(color) : null;
				swatch.IsVisible = shown != null;
				label.Text = (chosen, themeColor) switch
				{
					({ } value, _) => ColorSettings.ToHex(value),
					(null, { } theme) => $"{ColorSettings.ToHex(theme)} · {defaultName}",
					_ => defaultName,
				};
				preview.Background = swatch.Background;
				hex.Text = chosen is { } c ? ColorSettings.ToHex(c) : "";
				hex.PlaceholderText = themeColor is { } t ? ColorSettings.ToHex(t) : "#RRGGBB";
				problem.IsVisible = false;
				if (updatePicker && shown is { } pick)
				{
					picker.SetColor(pick);
				}
			}

			button.GetResourceObservable(defaultResource).Subscribe(new Avalonia.Reactive.AnonymousObserver<object?>(value =>
			{
				themeColor = value switch
				{
					Color color => color,
					ISolidColorBrush brush => Color.FromRgb(brush.Color.R, brush.Color.G, brush.Color.B),
					_ => null,
				};
				Show();
			}));

			void Choose(Color? color, bool fromPicker = false)
			{
				set(color);
				Show(updatePicker: !fromPicker);
			}

			picker.ColorChanged += color => Choose(color, fromPicker: true);

			void UseHex()
			{
				var text = hex.Text?.Trim();
				var current = get();
				if (string.IsNullOrEmpty(text) ? current == null : ColorSettings.TryParse(text) is { } same && same == current)
				{
					problem.IsVisible = false;
					return; // Unchanged (e.g. just leaving the box).
				}

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

			hex.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
				{
					e.Handled = true;
					UseHex();
				}
			};
			hex.LostFocus += (_, _) => UseHex();

			// Ready colours, and the theme's own.
			var ready = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4, MaxWidth = 260 };
			foreach (var code in Swatches)
			{
				var color = Color.Parse(code);
				var pick = new Button
				{
					Padding = new Thickness(0),
					MinWidth = 0,
					MinHeight = 0,
					Content = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(color) },
				};
				ToolTip.SetTip(pick, code);
				pick.Click += (_, _) => Choose(color);
				ready.Children.Add(pick);
			}

			var useDefault = new Button { Content = $"Default ({defaultName})" };
			useDefault.Click += (_, _) => Choose(null);

			button.Flyout = new Flyout
			{
				Placement = PlacementMode.BottomEdgeAlignedLeft,
				Content = new StackPanel
				{
					Spacing = 12,
					Children =
					{
						picker,
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { preview, hex } },
						problem,
						ready,
						useDefault,
					},
				},
			};

			Show();
			// Changed on another of the user's devices while this shows.
			void OnChangedElsewhere() => Show();
			button.AttachedToVisualTree += (_, _) => UserSettings.Changed += OnChangedElsewhere;
			button.DetachedFromVisualTree += (_, _) => UserSettings.Changed -= OnChangedElsewhere;
			return new DockPanel
			{
				Children =
				{
					// The colour's button at the right, as the other settings have their control.
					Dock(button),
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

			static Control Dock(Control control)
			{
				control.VerticalAlignment = VerticalAlignment.Center;
				control.Margin = new Thickness(16, 0, 0, 0);
				DockPanel.SetDock(control, Avalonia.Controls.Dock.Right);
				return control;
			}
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

	/// <summary>Display settings, read from and saved to <see cref="App.Settings"/>.</summary>
	public static class DisplaySettings
	{
		/// <summary>Whether the scroll view shows the song's name and details above the lyrics. Off unless turned on.</summary>
		public static bool ShowSongHeader
		{
			get => App.Settings.Get(SettingKeys.ShowSongHeader) == "true";
			set
			{
				App.Settings.Set(SettingKeys.ShowSongHeader, value ? "true" : null);
				ShowSongHeaderChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="ShowSongHeader"/> changes.</summary>
		public static event Action? ShowSongHeaderChanged;

		/// <summary>The smaller of the window's width and height at which <see cref="DynamicZoom"/> is 100 %.</summary>
		public const double DynamicZoomSize = 500;

		/// <summary>
		/// Whether the whole app is sized to the window (see <see cref="UiScale"/>): the smaller of its width and height,
		/// where <see cref="DynamicZoomSize"/> is 100 %. On unless turned off.
		/// </summary>
		public static bool DynamicZoom
		{
			get => App.Settings.Get(SettingKeys.DynamicZoom) != "false";
			set
			{
				App.Settings.Set(SettingKeys.DynamicZoom, value ? "true" : "false");
				DynamicZoomChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="DynamicZoom"/> changes.</summary>
		public static event Action? DynamicZoomChanged;
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
