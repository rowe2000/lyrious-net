using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>The app's settings, kept on this device (<see cref="App.Settings"/>). Changes apply at once.</summary>
	public sealed class SettingsView : UserControl
	{
		/// <param name="bandName">The current band's name, for its settings' heading. None (no band): no band settings.</param>
		/// <param name="bandSettings">The current band's settings (saved with the band, the same for all its members), shown under "Band settings".</param>
		/// <param name="bandMidiExpressions">The band's MIDI expressions as bands kept them, which count until the member saves their own.</param>
		/// <param name="bandMidiCommands">The band's MIDI commands as bands kept them, which count until the member saves their own.</param>
		/// <param name="memberName">The logged-in member's name, for the User check box's tooltip.</param>
		public SettingsView(string? bandName = null, Control? bandSettings = null, string? bandMidiExpressions = null, string? bandMidiCommands = null, string? memberName = null)
		{
			var capoMode = new ToggleSwitch
			{
				IsChecked = DisplaySettings.CapoMode,
				OnContent = L.T("On"),
				OffContent = L.T("Off"),
			};
			capoMode.IsCheckedChanged += (_, _) => DisplaySettings.CapoMode = capoMode.IsChecked == true;
			var transposeButton = new ToggleSwitch
			{
				IsChecked = DisplaySettings.TransposeButton,
				OnContent = L.T("On"),
				OffContent = L.T("Off"),
			};
			transposeButton.IsCheckedChanged += (_, _) =>
			{
				DisplaySettings.TransposeButton = transposeButton.IsChecked == true;
				capoRow!.IsVisible = DisplaySettings.TransposeButton;
			};
			var chordGridButton = new ToggleSwitch
			{
				IsChecked = DisplaySettings.ChordGridButton,
				OnContent = L.T("On"),
				OffContent = L.T("Off"),
			};
			chordGridButton.IsCheckedChanged += (_, _) => DisplaySettings.ChordGridButton = chordGridButton.IsChecked == true;
			Control? chordGridRow = null;

			var dynamicZoom = new ToggleSwitch
			{
				IsChecked = DisplaySettings.DynamicZoom,
				OnContent = L.T("On"),
				OffContent = L.T("Off"),
			};
			dynamicZoom.IsCheckedChanged += (_, _) => DisplaySettings.DynamicZoom = dynamicZoom.IsChecked == true;

			// The language: the device's, or one chosen; the screens are made anew in it at once.
			var language = new SettingComboBox { MinWidth = 100 };
			foreach (var choice in Localization.Language.Choices)
			{
				language.Items.Add(new ComboBoxItem { Content = choice.Code == "" ? Localization.L.T(choice.Name) : choice.Name, Tag = choice.Code });
			}

			language.SelectedIndex = Math.Max(0, System.Linq.Enumerable.ToList(Localization.Language.Choices).FindIndex(choice => choice.Code == Localization.Language.Chosen));
			language.SelectionChanged += (_, _) =>
			{
				if (language.SelectedItem is ComboBoxItem { Tag: string code } && code != Localization.Language.Chosen)
				{
					// After the list has closed: the screens (this one too) are made anew.
					Avalonia.Threading.Dispatcher.UIThread.Post(() => Localization.Language.Chosen = code);
				}
			};

			var scale = InterfaceSizeChoice();

			// The theme: dark (the default), light, or the computer's; applied at once.
			var themes = new (string Value, string Name)[] { (ThemeSettings.Dark, L.T("Dark")), (ThemeSettings.Light, L.T("Light")), (ThemeSettings.System, L.T("Same as the computer")) };
			var theme = new SettingComboBox { MinWidth = 100 };
			foreach (var (value, name) in themes)
			{
				theme.Items.Add(new ComboBoxItem { Content = name, Tag = value });
			}

			theme.SelectedIndex = Math.Max(0, Array.FindIndex(themes, choice => choice.Value == ThemeSettings.Chosen));
			theme.SelectionChanged += (_, _) =>
			{
				if (theme.SelectedItem is ComboBoxItem { Tag: string value } && value != ThemeSettings.Chosen)
				{
					ThemeSettings.Chosen = value;
				}
			};

			// What band sync (the band icon in the play toolbar) changes here when another member changes it.
			var bandSyncSection = Section(L.T("Band sync"));
			ToolTip.SetTip(bandSyncSection, Explained(L.T("With band sync on (the band icon in the play toolbar), what follows the band when another member changes it. What you change is still sent to the band. Yours on all your devices.")));

			// A switch for one of them, saved as soon as it's changed.
			ToggleSwitch FollowSwitch(Func<bool> get, Action<bool> set)
			{
				var toggle = new ToggleSwitch { IsChecked = get(), OnContent = L.T("On"), OffContent = L.T("Off") };
				toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
				return toggle;
			}

			var colorsSection = Section(L.T("Colors"));
			ToolTip.SetTip(colorsSection, Explained(L.T("Yours on all your devices.")));
			var form = new StackPanel
			{
				Spacing = 16,
				Children =
				{
					Section(L.T("Display")),
					Scoped(SettingScope.User, Setting(
						L.T("Language"),
						L.T("The language of the app. Yours on all your devices."),
						language)),
					Scoped(SettingScope.User, Setting(
						L.T("Theme"),
						L.T("Dark, light, or the same as the computer (following it when it changes). Yours on all your devices."),
						theme)),
					Scoped(SettingScope.Device, Setting(
						L.T("Interface size"),
						L.T("Makes the whole app larger or smaller: text, buttons, icons and spacing."),
						scale)),
					Scoped(SettingScope.User, chordGridRow = Setting(
						L.T("Chord grid button"),
						L.T("Shows the button on the lyrics page that shows a song's chords as a grid, by bar. Off, the chords show over the words. Yours on all your devices."),
						chordGridButton)),
					Scoped(SettingScope.Device, Setting(
						L.T("Dynamic zoom"),
						L.T("Sizes the whole app, lyrics included, to the window: the smaller of its width and height sets the zoom, where 500 px is 100 %. The interface size above and the lyrics' own zoom apply on top."),
						dynamicZoom)),
					Section(L.T("Playback")),
					Scoped(SettingScope.User, Setting(
						L.T("Transpose button"),
						L.T("Shows the button on the lyrics page that moves a song's chords for you only (a transpose or a capo). Yours on all your devices."),
						transposeButton)),
					// Only with the transpose button (where it counts); in a panel of its own, as the filter
					// above shows and hides the rows of the form itself.
					Scoped(SettingScope.User, new Border
					{
						Child = capoRow = Setting(
							L.T("Count my transpose as a capo"),
							L.T("On: + in the transpose dialog puts the capo a fret higher, which moves the chords down (capo +1 makes G F#). Off: a transpose in semitones, which moves them up. Yours on all your devices."),
							capoMode),
					}),
					bandSyncSection,
					Scoped(SettingScope.User, Setting(
						L.T("Follow the band's setlist"),
						L.T("When another member picks the band's setlist, yours changes to it too. Yours on all your devices."),
						FollowSwitch(() => PlaybackSettings.FollowSetlist, on => PlaybackSettings.FollowSetlist = on))),
					Scoped(SettingScope.User, Setting(
						L.T("Follow the band's song"),
						L.T("When another member picks the band's song, yours changes to it too (in your setlist, or the band's if you follow that). Yours on all your devices."),
						FollowSwitch(() => PlaybackSettings.FollowSong, on => PlaybackSettings.FollowSong = on))),
					Scoped(SettingScope.User, Setting(
						L.T("Follow play and stop"),
						L.T("When another member presses play or stop, your lyrics start or stop scrolling too. Yours on all your devices."),
						FollowSwitch(() => PlaybackSettings.FollowPlayback, on => PlaybackSettings.FollowPlayback = on))),
					Scoped(SettingScope.User, Setting(
						L.T("Follow the place in the song"),
						L.T("When another member presses play or stop, or goes back to the start, your lyrics go to the same place in the song. Yours on all your devices."),
						FollowSwitch(() => PlaybackSettings.FollowPosition, on => PlaybackSettings.FollowPosition = on))),
					colorsSection,
					ColorSetting(
						L.T("Highlight"),
						L.T("The current song, setlist and band, the selected row's outline, and the main buttons."),
						() => ColorSettings.Highlight,
						color => ColorSettings.Highlight = color,
						L.T("Theme purple"),
						"SystemAccentColor"),
					ColorSetting(
						L.T("Chords"),
						L.T("Chords above the lyrics, and the bar beside a chorus."),
						() => ColorSettings.Chord,
						color => ColorSettings.Chord = color,
						L.T("Theme colour"),
						LyriousTheme.ChordBrush),
					ColorSetting(
						L.T("Heading 1"),
						L.T("The largest headings in the lyrics (\"# \"), such as the song's title."),
						() => ColorSettings.Heading1,
						color => ColorSettings.Heading1 = color,
						L.T("Text colour"),
						LyriousTheme.Heading1Brush),
					ColorSetting(
						L.T("Heading 2"),
						L.T("Headings in the lyrics written \"## \", such as Verse and Chorus."),
						() => ColorSettings.Heading2,
						color => ColorSettings.Heading2 = color,
						L.T("Theme colour"),
						LyriousTheme.Heading2Brush),
					ColorSetting(
						L.T("Other headings"),
						L.T("Smaller headings in the lyrics (\"### \" and more)."),
						() => ColorSettings.Heading,
						color => ColorSettings.Heading = color,
						L.T("Theme colour"),
						LyriousTheme.HeadingBrush),
				},
			};

			// The current band's settings: saved with the band, not on this device. (Importing into its
			// songbook and clearing it are on the songbook page.)
			if (bandName != null && bandSettings != null)
			{
				var bandSection = Section(L.T("Band settings · {0}", bandName));
				ToolTip.SetTip(bandSection, Explained(L.T("Saved with the band: the same for all its members.")));
				form.Children.Add(bandSection);
				form.Children.Add(Scoped(SettingScope.Band, bandSettings));
			}

			// Where the app can be full screen (desktop, most browsers): after the chord grid button.
			if (FullScreen.IsSupported)
			{
				var fullScreen = new ToggleSwitch { IsChecked = DisplaySettings.FullScreen, OnContent = L.T("On"), OffContent = L.T("Off") };
				fullScreen.IsCheckedChanged += (_, _) => DisplaySettings.FullScreen = fullScreen.IsChecked == true;
				form.Children.Insert(form.Children.IndexOf(chordGridRow!) + 1, Scoped(SettingScope.Device, Setting(
					L.T("Full screen"),
					L.T("Shows the app on the whole screen, without the window's frame or the browser's bars. In a browser it starts at your first tap or key. F11 turns it on and off too. On this device."),
					fullScreen)));
			}

			capoRow.IsVisible = DisplaySettings.TransposeButton;

			// The MIDI module (see MidiSetup), before the colours: whether it's on, and while it is, its settings.
			var (useMidi, midiSettings) = MidiSetup.Create(bandMidiExpressions, bandMidiCommands);
			var midiAt = form.Children.IndexOf(colorsSection);
			form.Children.Insert(midiAt, Section("MIDI"));
			form.Children.Insert(midiAt + 1, Scoped(SettingScope.User, useMidi));
			// (In a panel of its own: the filter above shows and hides the rows of the form itself.)
			form.Children.Insert(midiAt + 2, Scoped(SettingScope.User, new Border { Child = midiSettings }));

			// Scrolls when the settings don't fit (a short window or a large interface size).
			form.Margin = new Thickness(0, 0, 0, 16);
			scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

			// At the top (not scrolling): which settings show, by where they're kept.
			var filter = ScopeFilter(form, bandName, memberName);
			DockPanel.SetDock(filter, Dock.Top);
			Content = PageLayout.Page(L.T("Settings"), new DockPanel { Children = { filter, scroller } }, closable: true, icon: AppIcons.Settings);
		}

		private readonly ScrollViewer scroller;

		// The first of the capo settings (capo or transpose), to scroll to (see ScrollToCapo).
		private readonly Control capoRow;

		/// <summary>
		/// Once the page is laid out, scrolls it so the capo settings are at the top (as far as it
		/// scrolls), e.g. when it's opened from the transpose dialog.
		/// </summary>
		public void ScrollToCapo()
		{
			void OnLayout(object? sender, EventArgs e)
			{
				// Hidden (the user's settings aren't shown, see ScopeFilter, or no transpose button): nothing to scroll to.
				if (!capoRow.IsEffectivelyVisible)
				{
					scroller.LayoutUpdated -= OnLayout;
					return;
				}

				if (scroller.Viewport.Height <= 0 || capoRow.Bounds.Height <= 0 || scroller.Content is not Control form)
				{
					return; // Not laid out yet.
				}

				scroller.LayoutUpdated -= OnLayout;
				if (capoRow.TranslatePoint(default, form) is { } top)
				{
					var room = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
					scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(top.Y, 0, room));
				}
			}

			scroller.LayoutUpdated += OnLayout;
		}

		/// <summary>
		/// "Import from SongbookPro…": a checklist of what the import does (all checked at first:
		/// overwriting songs of the same name, the sets as setlists and replacing those of the same name,
		/// and each conversion to Lyrious' format), then a button that picks the backup and imports it
		/// so; what happened (or why it couldn't) shows below it.
		/// </summary>
		internal static Control ImportSetting(Func<Control, SongbookProImportOptions, Task<string?>> import)
		{
			CheckBox Task(string text, string? tip = null)
			{
				var box = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = true };
				if (tip != null)
				{
					ToolTip.SetTip(box, tip);
				}

				return box;
			}

			var lettersOnlyNames = Task(L.T("Remove everything but letters, digits and spaces from the song names (punctuation, symbols)"), L.T("e.g. \"01. Go Your Own Way (live) ✓\" becomes \"01 Go Your Own Way live\""));
			var overwriteSongs = Task(L.T("Overwrite songs with the same name"), L.T("Names are compared by their letters only: case, spaces, digits and punctuation don't count. Unchecked, those songs are kept as they are."));
			var importSetlists = Task(L.T("Import the sets as setlists"));
			var replaceSetlists = Task(L.T("Replace the songs of setlists with the same name"), L.T("Unchecked, a set with the name of an existing setlist is left out."));
			var commentsAsHeadings = Task(L.T("Comments such as {c: Verse 1} become headings (## Verse 1)"));
			var headingChords = Task(L.T("A chord first in a heading is capitalised and put on its text (# [d] Intro becomes # [D]Intro)"));
			// The artists whose songs get their name as a tag, typed (kept on this device for the next import).
			var artistNames = new TextBox
			{
				Text = App.Settings.Get(SettingKeys.ImportTaggedArtists) ?? "",
				PlaceholderText = L.T("e.g. Maria, Tobbe"),
				MinWidth = 200,
				HorizontalAlignment = HorizontalAlignment.Left,
			};
			artistNames.TextChanged += (_, _) => App.Settings.Set(SettingKeys.ImportTaggedArtists, string.IsNullOrWhiteSpace(artistNames.Text) ? null : artistNames.Text.Trim());
			// "Tags", with what it does as the tooltip of the label and the box.
			var artistTagsLabel = new TextBlock { Text = L.T("Tags:"), TextWrapping = TextWrapping.Wrap };
			var artistTagsTip = L.T("Songs by these artists get the name as a tag instead of as the artist. Names separated by spaces or commas; empty, no artist becomes a tag.");
			ToolTip.SetTip(artistTagsLabel, artistTagsTip);
			ToolTip.SetTip(artistNames, artistTagsTip);
			var artistTags = new StackPanel
			{
				Spacing = 4,
				Margin = new Thickness(0, 4, 0, 4),
				Children = { artistTagsLabel, artistNames },
			};
			var checkTags = Task(L.T("A ✓ in a name or the lyrics becomes the tag ✓"));
			// Replacing setlists only matters when the sets come along.
			replaceSetlists.Margin = new Thickness(24, 0, 0, 0);
			importSetlists.IsCheckedChanged += (_, _) => replaceSetlists.IsEnabled = importSetlists.IsChecked == true;

			static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
			var checklist = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					Heading(L.T("Songs")),
					new TextBlock { Text = L.T("New songs are added to the songbook. PDF songs are left out."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 13 },
					overwriteSongs,
					lettersOnlyNames,
					Heading(L.T("Setlists")),
					importSetlists,
					replaceSetlists,
					Heading(L.T("Convert to Lyrious format")),
					commentsAsHeadings,
					headingChords,
					artistTags,
					checkTags,
				},
			};

			var button = new Button { Content = L.T("Import from SongbookPro…"), Classes = { "accent" } };
			var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Opacity = 0.85 };
			button.Click += async (_, _) =>
			{
				var options = new SongbookProImportOptions(
					OverwriteSongs: overwriteSongs.IsChecked == true,
					ImportSetlists: importSetlists.IsChecked == true,
					ReplaceSetlists: replaceSetlists.IsChecked == true,
					CommentsAsHeadings: commentsAsHeadings.IsChecked == true,
					HeadingChords: headingChords.IsChecked == true,
					TaggedArtists: SongbookProImport.ArtistNames(artistNames.Text),
					CheckTags: checkTags.IsChecked == true,
					LettersOnlyNames: lettersOnlyNames.IsChecked == true);
				button.IsEnabled = false;
				status.IsVisible = true;
				status.Text = L.T("Importing…");
				try
				{
					var result = await import(button, options);
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
			};

			return new StackPanel
			{
				Spacing = 8,
				Children =
				{
					new TextBlock
					{
						Text = L.T("Adds the songs and sets of a SongbookPro backup (\"SongbookPro Backup.zip\" or .sbpbackup) to your band's songbook and setlists, doing what's checked. SongbookPro's own songs.db works too: its songs go into your private songbook, and each of its bands' into your band of that name (made if you're not in one):"),
						TextWrapping = TextWrapping.Wrap,
						Opacity = 0.7,
					},
					checklist,
					button,
					status,
				},
			};
		}

		/// <summary>
		/// "Clear songbook…": a button that asks first (in a popup, with a red Delete), then deletes every
		/// song in the band's songbook; what happened shows below it.
		/// </summary>
		internal static Control ClearSetting(Func<Task<string>> clear, string bandName)
		{
			// Red: it deletes every song.
			var button = PageLayout.DangerButton(L.T("Clear songbook…"));
			var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Opacity = 0.85 };
			var confirm = PageLayout.DangerButton(L.T("Delete all songs"));
			var cancel = new Button { Content = L.T("Cancel") };
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
							Text = L.T("Delete every song in {0}'s songbook? They're removed from every setlist too (the setlists stay, empty). This can't be undone.", bandName),
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
				status.Text = L.T("Deleting…");
				try
				{
					status.Text = await clear();
				}
				catch (Exception ex)
				{
					status.Text = L.T("Couldn't clear the songbook. {0}", ex.Message);
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
					new TextBlock { Text = L.T("Deletes all the songs in the songbook, for everyone in the band."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
					button,
					status,
				},
			};
		}

		/// <summary>
		/// "Remove duplicate songs…": a button that asks first (in a popup), then merges the songbook's
		/// songs whose names have the same letters (see <see cref="SongbookProImport.NameKey"/>); what
		/// happened shows below it.
		/// </summary>
		internal static Control RemoveDuplicatesSetting(Func<Task<string>> removeDuplicates)
		{
			var button = new Button { Content = L.T("Remove duplicate songs…") };
			var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Opacity = 0.85 };
			var confirm = PageLayout.DangerButton(L.T("Remove duplicates"));
			var cancel = new Button { Content = L.T("Cancel") };
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
							Text = L.T("Remove the duplicate songs? Of the songs whose names have the same letters (case, spaces, digits and punctuation don't count), the one with the most lyrics is kept; it takes the others' places in the setlists, and they're deleted. This can't be undone."),
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
				status.Text = L.T("Removing duplicates…");
				try
				{
					status.Text = await removeDuplicates();
				}
				catch (Exception ex)
				{
					status.Text = L.T("Couldn't remove the duplicates. {0}", ex.Message);
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
				Children =
				{
					new TextBlock { Text = L.T("Merges songs with the same name, comparing only their letters."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
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
			// Shown while any of its settings is (see ScopeFilter).
			Tag = SectionMark,
		};

		/// <summary>Where a setting is kept: on this device, with the user (on all their devices), or with the band.</summary>
		private enum SettingScope
		{
			Device,
			User,
			Band,
		}

		// Marks a section's heading (see Section).
		private static readonly object SectionMark = new();

		/// <summary>Marks a setting (its row) with where it's kept, for <see cref="ScopeFilter"/>.</summary>
		private static Control Scoped(SettingScope scope, Control row)
		{
			row.Tag = scope;
			return row;
		}

		/// <summary>
		/// Check boxes for where settings are kept (this device, the user, the band): only the settings of
		/// the checked ones show in <paramref name="form"/>, and a section's heading while any of its do. The
		/// choice lasts while the app runs (all checked again after a restart).
		/// </summary>
		/// <param name="bandName">The current band's name, for the Band check box's tooltip.</param>
		/// <param name="memberName">The member's name, for the User check box's tooltip.</param>
		// The kinds of settings unchecked in the filter, while the app runs (not kept over a restart).
		private static string[] hiddenScopes = [];

		private static Control ScopeFilter(Panel form, string? bandName, string? memberName)
		{
			var hidden = hiddenScopes;
			var boxes = new (SettingScope Scope, CheckBox Box)[]
			{
				(SettingScope.Device, new CheckBox { Content = L.T("Device") }),
				(SettingScope.User, new CheckBox { Content = L.T("User") }),
				(SettingScope.Band, new CheckBox { Content = L.T("Band") }),
			};
			// Whose settings each box shows: this device's, the member's (by name), the band's (by name).
			ToolTip.SetTip(boxes[0].Box, L.T("This device settings"));
			ToolTip.SetTip(boxes[1].Box, string.IsNullOrWhiteSpace(memberName) ? L.T("Your user settings") : L.T("{0} user settings", memberName));
			ToolTip.SetTip(boxes[2].Box, string.IsNullOrWhiteSpace(bandName) ? L.T("Band settings") : L.T("{0} band settings", bandName));

			void Show()
			{
				var shown = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Scope).ToHashSet();
				TextBlock? section = null;
				var anyInSection = false;
				foreach (var child in form.Children)
				{
					if (ReferenceEquals(child.Tag, SectionMark))
					{
						if (section != null)
						{
							section.IsVisible = anyInSection;
						}

						section = child as TextBlock;
						anyInSection = false;
					}
					else if (child.Tag is SettingScope scope)
					{
						child.IsVisible = shown.Contains(scope);
						anyInSection |= child.IsVisible;
					}
					else
					{
						anyInSection |= child.IsVisible;
					}
				}

				if (section != null)
				{
					section.IsVisible = anyInSection;
				}
			}

			// At least one kind is always shown: all of them hidden (as saved) shows them all.
			if (boxes.All(b => hidden.Contains(b.Scope.ToString())))
			{
				hidden = [];
			}

			foreach (var (scope, box) in boxes)
			{
				box.IsChecked = !hidden.Contains(scope.ToString());
				box.IsCheckedChanged += (_, _) =>
				{
					// The last one checked can't be unchecked: it's checked again at once.
					if (boxes.All(b => b.Box.IsChecked != true))
					{
						box.IsChecked = true;
						return;
					}

					hiddenScopes = boxes.Where(b => b.Box.IsChecked != true).Select(b => b.Scope.ToString()).ToArray();
					Show();
				};
			}

			Show();
			var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 16, Margin = new Thickness(0, 0, 0, 12) };
			foreach (var (_, box) in boxes)
			{
				row.Children.Add(box);
			}

			return row;
		}

		/// <summary>
		/// A colour setting: a button showing the colour and its code (for the theme's own, the colour
		/// <paramref name="defaultResource"/> holds, with <paramref name="defaultName"/>) that opens a
		/// popup to pick another: hue, saturation and lightness (see <see cref="HslColorPicker"/>), a
		/// box for "#RRGGBB", a row of ready colours, and Default. A change shows at once.
		/// </summary>
		private static Control ColorSetting(string name, string explanation, Func<Color?> get, Action<Color?> set, string defaultName, string defaultResource)
		{
			var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Gray, 0.6) };
			var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };

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
			var problem = new TextBlock { Text = L.T("Not a colour; write it as #RRGGBB."), IsVisible = false, TextWrapping = TextWrapping.Wrap };
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
					// A colour of one's own by its value; the theme's by its name only.
					({ } value, _) => ColorSettings.ToHex(value),
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

			var useDefault = new Button { Content = L.T("Default ({0})", defaultName) };
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
			// The colours are kept with the user.
			return new DockPanel
			{
				Tag = SettingScope.User,
				Children =
				{
					// The colour's button at the right, as the other settings have their control.
					Dock(button),
					SettingName(name, explanation),
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

		/// <summary>
		/// The interface size (see <see cref="UiScale"/>): a list of sizes, applied when picked, whose box
		/// can be typed in too: a whole percentage from 30 to 300 (only digits can be typed, never more than
		/// 300), applied with Enter or when the box is left; anything else gives the size back as it was.
		/// </summary>
		private static ComboBox InterfaceSizeChoice()
		{
			const int least = 30, most = 300;
			var scale = new SettingComboBox
			{
				IsEditable = true,
				Width = 110,
				ItemsSource = UiScale.Choices.Select(UiScale.Describe).ToList(),
				Text = UiScale.Describe(UiScale.Value),
			};
			// Its text in the middle of its height, as a text box's.
			PageLayout.CenterTypedText(scale, leftPadding: 10);

			// The percentage in the box ("125", "125%", "125 %"), if it's a whole number.
			static int? Percent(string? text)
				=> int.TryParse((text ?? "").Replace("%", "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

			void ShowCurrent() => scale.Text = UiScale.Describe(UiScale.Value);

			// The size in the box, if it's one (30–300 %); else the size back as it was.
			void Apply()
			{
				if (Percent(scale.Text) is >= least and <= most and var percent)
				{
					if (Math.Abs(UiScale.Value * 100 - percent) > 0.01)
					{
						UiScale.Value = percent / 100.0;
					}
				}

				ShowCurrent();
			}

			// Picked in the list: applied at once.
			scale.SelectionChanged += (_, _) =>
			{
				if (scale.IsDropDownOpen && scale.SelectedItem is string)
				{
					Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
				}
			};
			scale.DropDownClosed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Apply);

			// Typed: digits, without a 0 first, never more than 300, and a % after them (a space before it
			// too, "125 %"); checked before it's in the text, so only that can be typed.
			scale.AddHandler(InputElement.TextInputEvent, (_, e) =>
			{
				if (e.Source is not TextBox input || string.IsNullOrEmpty(e.Text))
				{
					return;
				}

				e.Handled = true;
				var text = input.Text ?? "";
				var start = Math.Clamp(Math.Min(input.SelectionStart, input.SelectionEnd), 0, text.Length);
				var end = Math.Clamp(Math.Max(input.SelectionStart, input.SelectionEnd), 0, text.Length);
				var typed = new string(e.Text.Where(c => char.IsAsciiDigit(c) || c is '%' or ' ').ToArray());
				var result = text[..start] + typed + text[end..];
				if (typed.Length == 0
				    || !System.Text.RegularExpressions.Regex.IsMatch(result, @"^[1-9][0-9]{0,2}( ?%| )?$")
				    || Percent(result) > most)
				{
					return;
				}

				input.Text = result;
				input.SelectionStart = input.SelectionEnd = input.CaretIndex = start + typed.Length;
			}, Avalonia.Interactivity.RoutingStrategies.Tunnel);

			// Enter, or the box left: the size typed.
			scale.AddHandler(InputElement.KeyDownEvent, (_, e) =>
			{
				if (e.Key == Key.Enter && !scale.IsDropDownOpen)
				{
					e.Handled = true;
					Apply();
				}
			}, Avalonia.Interactivity.RoutingStrategies.Tunnel);
			scale.LostFocus += (_, _) =>
			{
				if (!scale.IsKeyboardFocusWithin && !scale.IsDropDownOpen)
				{
					Apply();
				}
			};
			return scale;
		}

		/// <summary>
		/// The Yamaha keyboards that get the member's transpose (see <see cref="Midi.YamahaTransposeModels"/>):
		/// a drop-down of check boxes, one per model, saved at once; the button says which are chosen.
		/// A keyboard taken off the list gets its transpose back to 0, so it isn't left transposed.
		/// </summary>
		internal static Control YamahaTransposeChoice()
		{
			var button = new DropDownButton { MinWidth = 100 };
			void ShowChosen()
			{
				var chosen = Midi.YamahaTransposeModels;
				button.Content = chosen.Count == 0 ? L.T("None") : string.Join(", ", chosen);
			}

			var list = new StackPanel { Spacing = 4 };
			foreach (var model in Midi.YamahaModels)
			{
				var box = new CheckBox { Content = model, IsChecked = Midi.YamahaTransposeModels.Contains(model) };
				box.IsCheckedChanged += (_, _) =>
				{
					var models = Midi.YamahaTransposeModels.ToList();
					if (box.IsChecked == true)
					{
						if (!models.Contains(model))
						{
							models.Add(model);
						}
					}
					else if (models.Remove(model))
					{
						Midi.SendYamahaTranspose(0, [model]);
					}

					Midi.YamahaTransposeModels = models;
					ShowChosen();
				};
				list.Children.Add(box);
			}

			button.Flyout = new Flyout { Content = list, Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
			ShowChosen();
			return button;
		}

		/// <summary>One setting: its name (its explanation the name's tooltip), with its control at the right.</summary>
		internal static Control Setting(string name, string explanation, Control control)
		{
			control.VerticalAlignment = VerticalAlignment.Center;
			control.Margin = new Thickness(16, 0, 0, 0);
			DockPanel.SetDock(control, Dock.Right);
			return new DockPanel { Children = { control, SettingName(name, explanation) } };
		}

		/// <summary>A setting's name, its explanation shown when it's hovered (or held).</summary>
		internal static TextBlock SettingName(string name, string explanation)
		{
			var text = new TextBlock { Text = name, FontSize = 16, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
			ToolTip.SetTip(text, Explained(explanation));
			return text;
		}

		/// <summary>An explanation as a tooltip: wrapped, not as wide as the window.</summary>
		internal static TextBlock Explained(string explanation) => new() { Text = explanation, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
	}

	/// <summary>Display settings, read from and saved to <see cref="App.Settings"/>.</summary>
	public static class DisplaySettings
	{

		/// <summary>
		/// Whether the member counts their own transpose as a capo (see <see cref="SettingKeys.CapoMode"/>):
		/// capo n moves the chords n semitones down (capo +1 makes G F#), −11 to +11; else a transpose in
		/// semitones. Only how it's shown and stepped: a song keeps its transpose (capo n is the transpose −n).
		/// Kept with the user (see <see cref="UserSettings"/>); off unless turned on.
		/// </summary>
		public static bool CapoMode
		{
			get => App.Settings.Get(SettingKeys.CapoMode) == "true";
			set
			{
				App.Settings.Set(SettingKeys.CapoMode, value ? "true" : null);
				UserSettings.ChangedHere();
				CapoModeChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="CapoMode"/> is changed here (a change from another device raises <see cref="UserSettings.Changed"/>).</summary>
		public static event Action? CapoModeChanged;

		/// <summary>
		/// Whether the lyrics page has the transpose button (see <see cref="SettingKeys.TransposeButton"/>),
		/// for the member's own transpose or capo of a song. Kept with the user; off unless turned on.
		/// </summary>
		public static bool TransposeButton
		{
			get => App.Settings.Get(SettingKeys.TransposeButton) == "true";
			set
			{
				App.Settings.Set(SettingKeys.TransposeButton, value ? "true" : null);
				UserSettings.ChangedHere();
				TransposeButtonChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="TransposeButton"/> is changed here (a change from another device raises <see cref="UserSettings.Changed"/>).</summary>
		public static event Action? TransposeButtonChanged;

		/// <summary>
		/// Whether the lyrics page has the chord grid button (see <see cref="SettingKeys.ChordGridButton"/>);
		/// without it the lyrics show chords over the words. Kept with the user; on unless turned off.
		/// </summary>
		public static bool ChordGridButton
		{
			get => App.Settings.Get(SettingKeys.ChordGridButton) != "false";
			set
			{
				App.Settings.Set(SettingKeys.ChordGridButton, value ? null : "false");
				UserSettings.ChangedHere();
				ChordGridButtonChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="ChordGridButton"/> is changed here (a change from another device raises <see cref="UserSettings.Changed"/>).</summary>
		public static event Action? ChordGridButtonChanged;

		/// <summary>
		/// Whether the app is shown full screen (the desktop's window or the browser's page; see
		/// <see cref="Lyrious.UILib.FullScreen"/>; F11 turns it on and off too). This device only; off unless turned on.
		/// </summary>
		public static bool FullScreen
		{
			get => App.Settings.Get(SettingKeys.FullScreen) == "true";
			set
			{
				App.Settings.Set(SettingKeys.FullScreen, value ? "true" : null);
				FullScreenChanged?.Invoke();
			}
		}

		/// <summary>Raised when <see cref="FullScreen"/> changes.</summary>
		public static event Action? FullScreenChanged;

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
		/// <summary>
		/// Band sync (the globe in the play toolbar): follow the joined band's current song, setlist
		/// and play/stop, and send this member's. On unless turned off.
		/// </summary>
		public static bool BandSync
		{
			get => App.Settings.Get(SettingKeys.BandSync) != "false";
			set => App.Settings.Set(SettingKeys.BandSync, value ? "true" : "false");
		}

		/// <summary>Whether band sync changes this member's current setlist to the band's. Kept with the user; on unless turned off.</summary>
		public static bool FollowSetlist
		{
			get => App.Settings.Get(SettingKeys.BandSyncSetlist) != "false";
			set => SetFollow(SettingKeys.BandSyncSetlist, value);
		}

		/// <summary>Whether band sync changes this member's current song to the band's. Kept with the user; on unless turned off.</summary>
		public static bool FollowSong
		{
			get => App.Settings.Get(SettingKeys.BandSyncSong) != "false";
			set => SetFollow(SettingKeys.BandSyncSong, value);
		}

		/// <summary>Whether band sync starts and stops this member's lyrics with the band's play and stop. Kept with the user; on unless turned off.</summary>
		public static bool FollowPlayback
		{
			get => App.Settings.Get(SettingKeys.BandSyncPlayback) != "false";
			set => SetFollow(SettingKeys.BandSyncPlayback, value);
		}

		/// <summary>
		/// Whether band sync moves this member's lyrics to where the member who plays, stops or goes
		/// back to the start is in the song. Kept with the user; on unless turned off.
		/// </summary>
		public static bool FollowPosition
		{
			get => App.Settings.Get(SettingKeys.BandSyncPosition) != "false";
			set => SetFollow(SettingKeys.BandSyncPosition, value);
		}

		// On is kept as nothing (the default), off as "false"; sent with the user's other settings.
		private static void SetFollow(string key, bool value)
		{
			App.Settings.Set(key, value ? null : "false");
			UserSettings.ChangedHere();
		}
	}
}
