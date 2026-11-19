using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The band's songbook, to pick a song to show; the current song is highlighted. The "+" menu
	/// adds a song: an empty one (named here, then edited), one imported from a text or ChordPro
	/// file, or one found on the internet. A click selects a song and shows its toolbar (open,
	/// add to a setlist, edit, delete); a double click opens it. The search box above the list filters it by name
	/// and lyrics as you type.
	/// </summary>
	public sealed class SongsView : UserControl
	{
		private readonly TextBlock status = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Avalonia.Thickness(0, 0, 0, 16) };

		// The search text and chosen tags, kept from one showing of the page to the next while the
		// app runs.
		private static string savedSearch = "";
		private static readonly HashSet<string> savedTags = new(StringComparer.CurrentCultureIgnoreCase);
		private static readonly HashSet<string> savedWithoutTags = new(StringComparer.CurrentCultureIgnoreCase);

		/// <summary>Raised when the user opens a song (double click, or the open button).</summary>
		public event Action<SongDetails>? SongSelected;

		/// <summary>Raised with the name when the user adds an empty song (to edit next).</summary>
		public event Action<string>? EmptySongRequested;

		/// <summary>Raised with a name and the lyrics when the user imports a file.</summary>
		public event Action<string, string>? SongImported;

		/// <summary>Raised when the user wants to edit a song (name, key, tempo, beat, lyrics).</summary>
		public event Action<SongDetails>? SongEditRequested;

		/// <summary>Raised when the user confirms deleting a song.</summary>
		public event Action<SongDetails>? SongDeleteRequested;

		/// <summary>Raised when the user chooses a setlist to add a song to (at its end).</summary>
		public event Action<SongDetails, SetlistSummary>? SongAddToSetlistRequested;

		/// <summary>Gets the band's setlists, for the menu of the add-to-setlist button.</summary>
		public Func<Task<IReadOnlyList<SetlistSummary>>>? LoadSetlists { get; set; }

		// A list (three lines) with a plus at its lower right.
		private const string AddToSetlistIcon = "M4,7 H15 M4,12 H12 M4,17 H10 M17,13 V21 M13,17 H21";

		/// <summary>Raised when the user wants to find a song on the internet.</summary>
		public event Action? InternetSearchRequested;

		/// <summary>
		/// Raised with the songs the list shows (searched and filtered as it is) when the user wants them
		/// as the setlist in the right drawer, just for now: it isn't saved.
		/// </summary>
		public event Action<IReadOnlyList<SongDetails>>? ShowAsSetlistRequested;

		/// <summary>Raised when the user confirms deleting several songs at once (the checked ones, or the selected one).</summary>
		public event Action<IReadOnlyList<SongDetails>>? SongsDeleteRequested;

		/// <summary>Raised with a name and the songs (in order) when the user saves them as a new setlist.</summary>
		public event Action<string, IReadOnlyList<SongDetails>>? SaveAsSetlistRequested;

		// The songs checked (for the setlist tool): kept while the list is searched and filtered, and
		// from one showing of the page to the next while the app runs (as the search and tags are).
		private static readonly HashSet<Guid> checkedSongs = [];

		// The check boxes of the songs listed now.
		private readonly List<CheckBox> checkBoxes = [];

		// This songbook's songs (the checks of another songbook's don't count here).
		private readonly HashSet<Guid> songIds;

		private bool AnyChecked() => checkedSongs.Overlaps(songIds);

		// A list (three lines) with a play triangle at its lower right.
		private const string AsSetlistIcon = "M4,6 H20 M4,11 H14 M4,16 H10 M15,14 L21,17.5 L15,21 Z";

		/// <summary>The band whose songbook this is.</summary>
		public GroupSummary Group { get; }

		/// <summary>The song shown as the current one (when the page was made).</summary>
		public Guid? CurrentSongId { get; }

		/// <summary>Raised when the user chooses another songbook (the private one or the joined band's).</summary>
		public event Action<GroupSummary>? SongbookChosen;

		/// <param name="songbooks">The songbooks to choose between (e.g. the private one and the joined band's), shown as buttons above the list; none hides them.</param>
		/// <param name="playingSongbookId">The songbook (its band's id) whose songs are played as the setlist now; null if none.</param>
		public SongsView(GroupSummary group, IReadOnlyList<SongDetails> songs, Guid? currentSongId, IReadOnlyList<GroupSummary>? songbooks = null, Guid? playingSongbookId = null)
		{
			Group = group;
			songIds = songs.Select(song => song.Id).ToHashSet();
			CurrentSongId = currentSongId;
			// The list, rebuilt as the search text or the chosen tags change.
			var list = new ContentControl();
			// The search text and tags as they were when the page was last left.
			var search = PageLayout.WithClearButton(new TextBox { Text = savedSearch, PlaceholderText = "Search songs, artists, lyrics and tags", Margin = new Avalonia.Thickness(0, 0, 0, 16) });
			DockPanel.SetDock(search, Dock.Top);
			// A magnifying glass at the left in the box, dimmed like its placeholder.
			var searchIcon = LineIcon(SearchIcon, 16);
			searchIcon.Margin = new Avalonia.Thickness(10, 0, 0, 0);
			searchIcon.Opacity = 0.6;
			searchIcon.IsHitTestVisible = false;
			search.InnerLeftContent = searchIcon;
			// The tags chosen to filter by: a song is shown when it has all the tags chosen "with" and
			// none of those chosen "without" (longclicked). Of the tags chosen before, those this
			// songbook has.
			var allTags = SongTags.AllOf(songs);
			var chosenTags = new HashSet<string>(allTags.Where(savedTags.Contains), StringComparer.CurrentCultureIgnoreCase);
			var withoutTags = new HashSet<string>(allTags.Where(savedWithoutTags.Contains), StringComparer.CurrentCultureIgnoreCase);
			// The songs the list shows now, for the setlist tool.
			IReadOnlyList<SongDetails> shownSongs = songs;

			void ShowList()
			{
				var text = search.Text?.Trim() ?? "";
				var shown = (IReadOnlyList<SongDetails>)songs
					.Where(song => text.Length == 0 || Matches(song, text))
					.Where(song => chosenTags.All(tag => SongTags.Has(song, tag)) && !withoutTags.Any(tag => SongTags.Has(song, tag)))
					.ToArray();
				string emptyText;
				if (songs.Count == 0)
				{
					emptyText = "There are no songs in the songbook yet. Add one with +.";
				}
				else if (chosenTags.Count == 0 && withoutTags.Count == 0)
				{
					emptyText = $"No songs match \"{text}\".";
				}
				else
				{
					var filter = string.Join(", ", chosenTags.Concat(withoutTags.Select(tag => $"not {tag}")));
					emptyText = text.Length == 0
						? $"No songs fit these tags: {filter}."
						: $"No songs with the tags {filter} match \"{text}\".";
				}

				shownSongs = shown;
				list.Content = SongList(shown, currentSongId, emptyText);

				// Kept for the next time the page shows. Chosen tags this songbook doesn't have stay
				// chosen (for a songbook that has them).
				savedSearch = search.Text ?? "";
				savedTags.ExceptWith(allTags);
				savedTags.UnionWith(chosenTags);
				savedWithoutTags.ExceptWith(allTags);
				savedWithoutTags.UnionWith(withoutTags);
			}

			var tagFilter = TagFilter(allTags, chosenTags, withoutTags, ShowList);

			search.TextChanged += (_, _) => ShowList();
			// The search box has the focus when the page shows, so typing searches at once (the text
			// kept from last time stays, with the cursor at its end).
			AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				search.Focus();
				search.CaretIndex = search.Text?.Length ?? 0;
			}, Avalonia.Threading.DispatcherPriority.Background);
			ShowList();

			var (nameRow, showNameRow) = PageLayout.NameRow("Name of the new song", name => EmptySongRequested?.Invoke(name));
			DockPanel.SetDock(status, Dock.Top);

			// The songs the setlist tool uses: the checked ones (in the songbook's order), else all listed.
			IReadOnlyList<SongDetails> ChosenSongs()
				=> AnyChecked() ? songs.Where(song => checkedSongs.Contains(song.Id)).ToArray() : shownSongs;

			// The setlist tool: a popup to play the songs (shown as the setlist in the right drawer, not
			// saved), or to name a new setlist of them right there; and to clear the checks.
			var asSetlistButton = HomeView.IconButton(AsSetlistIcon, "Use these songs as a setlist");
			asSetlistButton.IsEnabled = songs.Count > 0;
			// (Made on each click, with the songs chosen then.)
			asSetlistButton.Click += (_, _) =>
			{
				var chosen = ChosenSongs();
				var plural = chosen.Count == 1 ? "" : "s";
				var which = AnyChecked()
					? $"The {chosen.Count} checked song{plural}"
					: chosen.Count == songs.Count ? $"All {chosen.Count} song{plural}" : $"The {chosen.Count} listed song{plural}";
				var popup = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };

				// Play: shown as the setlist in the right drawer, not saved.
				var play = PopupItem("Just play them", PlayIcon);
				play.IsEnabled = chosen.Count > 0;
				play.Click += (_, _) =>
				{
					popup.Hide();
					ShowAsSetlistRequested?.Invoke(chosen);
				};

				// A new setlist: "Create a setlist" shows its name box, then ✓ (or Enter) makes it; the
				// popup stays open until it's made (or closed with its ✕, Escape or a click outside).
				// As tall as the ✓ and ✕ beside it, with its text in the middle.
				var name = new TextBox { PlaceholderText = "Name of the new setlist", MinWidth = 200, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
				var ok = PageLayout.OkButton("Create the setlist");
				ok.IsEnabled = false;
				// The popup's ✕, at its top right: closes it.
				var close = PageLayout.CancelButton("Close");
				ok.Margin = new Avalonia.Thickness(8, 0, 0, 0);
				DockPanel.SetDock(ok, Dock.Right);
				var nameRow = new DockPanel { IsVisible = false, Children = { ok, name } };
				var create = PopupItem("Create a setlist", NewSetlistIcon);
				create.IsEnabled = chosen.Count > 0;
				// The name box takes the button's place, with its icon at the left in the box.
				var nameIcon = LineIcon(NewSetlistIcon, 18);
				nameIcon.Margin = new Avalonia.Thickness(10, 0, 0, 0);
				nameIcon.Opacity = 0.7;
				nameIcon.IsHitTestVisible = false;
				name.InnerLeftContent = nameIcon;
				create.Click += (_, _) =>
				{
					create.IsVisible = false;
					nameRow.IsVisible = true;
					Avalonia.Threading.Dispatcher.UIThread.Post(() => name.Focus(), Avalonia.Threading.DispatcherPriority.Background);
				};

				void Create()
				{
					var text = name.Text?.Trim();
					if (string.IsNullOrEmpty(text) || chosen.Count == 0)
					{
						return;
					}

					popup.Hide();
					SaveAsSetlistRequested?.Invoke(text, chosen);
					// Made: the songs in it are checked no more.
					checkedSongs.ExceptWith(chosen.Select(song => song.Id));
					foreach (var box in checkBoxes)
					{
						box.IsChecked = false;
					}

					DimCheckBoxes();
				}

				name.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
				name.KeyDown += (_, e) =>
				{
					if (e.Key == Avalonia.Input.Key.Enter)
					{
						e.Handled = true;
						Create();
					}
					else if (e.Key == Avalonia.Input.Key.Escape)
					{
						e.Handled = true;
						popup.Hide();
					}
				};
				ok.Click += (_, _) => Create();
				close.Click += (_, _) => popup.Hide();
				DockPanel.SetDock(close, Dock.Right);

				var panel = new StackPanel
				{
					Spacing = 6,
					MinWidth = 256,
					Children =
					{
						// Which songs (all, the listed ones, or the checked ones), and the ✕ at the right.
						new DockPanel
						{
							Children =
							{
								close,
								new TextBlock { Text = which, Opacity = 0.8, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(10, 0, 0, 0) },
							},
						},
						play,
						create,
						nameRow,
					},
				};

				if (AnyChecked())
				{
					var clear = PopupItem("Clear the checks", UncheckedIcon);
					clear.Click += (_, _) =>
					{
						popup.Hide();
						checkedSongs.ExceptWith(songIds);
						foreach (var box in checkBoxes)
						{
							box.IsChecked = false;
						}

						DimCheckBoxes();
					};
					panel.Children.Add(new Separator());
					panel.Children.Add(clear);
				}

				// Deleting: the checked songs, else the selected one (never all the listed ones, so a
				// slip can't empty the songbook). Asked again first, in the popup.
				var selected = (list.Content as SelectableList<SongDetails>)?.Selected;
				IReadOnlyList<SongDetails> toDelete = AnyChecked()
					? songs.Where(song => checkedSongs.Contains(song.Id)).ToArray()
					: selected != null ? [selected] : [];
				if (toDelete.Count > 0)
				{
					var what = toDelete.Count == 1 ? $"'{toDelete[0].Name}'" : $"the {toDelete.Count} checked songs";
					var delete = PopupItem(toDelete.Count == 1 ? $"Delete '{toDelete[0].Name}'…" : $"Delete the {toDelete.Count} checked songs…", TrashIcon);
					PageLayout.RedOnHover(delete);
					var confirm = PageLayout.DangerButton("Delete");
					var keep = PageLayout.CancelButton("Don't delete");
					var question = new TextBlock
					{
						Text = $"Delete {what}? {(toDelete.Count == 1 ? "It's" : "They're")} also removed from every setlist, and this can't be undone.",
						TextWrapping = TextWrapping.Wrap,
						MaxWidth = 300,
						Margin = new Avalonia.Thickness(10, 0, 0, 0),
					};
					var confirmButtons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(10, 0, 0, 0), Children = { confirm, keep } };
					var confirmRow = new StackPanel { Spacing = 6, IsVisible = false, Children = { question, confirmButtons } };
					delete.Click += (_, _) => confirmRow.IsVisible = true;
					keep.Click += (_, _) => confirmRow.IsVisible = false;
					confirm.Click += (_, _) =>
					{
						popup.Hide();
						checkedSongs.ExceptWith(toDelete.Select(song => song.Id));
						SongsDeleteRequested?.Invoke(toDelete);
					};
					panel.Children.Add(new Separator());
					panel.Children.Add(delete);
					panel.Children.Add(confirmRow);
				}

				popup.Content = panel;
				// Escape closes this popup first, before anything on the page (its ✕ closes the page):
				// seen on the window on its way in (tunnelling), so before the page's own Escape.
				var window = TopLevel.GetTopLevel(this);
				void OnEscape(object? sender, Avalonia.Input.KeyEventArgs e)
				{
					if (e.Key == Avalonia.Input.Key.Escape && popup.IsOpen)
					{
						e.Handled = true;
						popup.Hide();
					}
				}

				window?.AddHandler(KeyDownEvent, OnEscape, Avalonia.Interactivity.RoutingStrategies.Tunnel);
				// And when the keyboard is in the popup itself (a window of its own on the desktop).
				panel.AddHandler(KeyDownEvent, OnEscape, Avalonia.Interactivity.RoutingStrategies.Tunnel);
				popup.Closed += (_, _) => window?.RemoveHandler(KeyDownEvent, OnEscape);
				popup.ShowAt(asSetlistButton);
			};

			var addButton = PageLayout.AddButton("Add song");
			var empty = new MenuItem { Header = "New empty song…" };
			empty.Click += (_, _) => showNameRow();
			var import = new MenuItem { Header = "Import from file…" };
			import.Click += (_, _) => _ = ImportAsync();
			var internet = new MenuItem { Header = "Find on the internet…" };
			internet.Click += (_, _) => InternetSearchRequested?.Invoke();
			addButton.Flyout = new MenuFlyout { Items = { empty, import, internet } };

			Content = PageLayout.Page(
				"Songbook",
				new DockPanel { Children = { SongbookChooser(group, songbooks ?? [], playingSongbookId), nameRow, status, search, tagFilter, list } },
				titleAction: new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Children = { asSetlistButton, addButton } },
				closable: true,
				icon: AppIcons.Songbook);
		}

		/// <summary>
		/// The songbooks to choose between as buttons flowing left to right, as wide as their names;
		/// the shown one is highlighted. Hidden when there's nothing to choose between.
		/// </summary>
		private Control SongbookChooser(GroupSummary shown, IReadOnlyList<GroupSummary> songbooks, Guid? playingSongbookId)
		{
			var chooser = new WrapPanel
			{
				Orientation = Avalonia.Layout.Orientation.Horizontal,
				ItemSpacing = 8,
				LineSpacing = 8,
				Margin = new Avalonia.Thickness(0, 0, 0, 16),
				IsVisible = songbooks.Count > 1,
			};
			foreach (var songbook in songbooks)
			{
				Control name = new TextBlock { Text = songbook.Name, FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
				// The songbook played as the setlist (see the setlist tool) has the note of the current one.
				if (songbook.Id == playingSongbookId)
				{
					var note = PageLayout.CurrentCheck("Played as the setlist");
					note.Margin = new Avalonia.Thickness(8, -2, 0, -2);
					name = new DockPanel { Children = { note, name } };
				}

				var button = new Button
				{
					Content = name,
					Padding = new Avalonia.Thickness(12, 6),
				};
				if (songbook.Id == shown.Id)
				{
					button.Classes.Add("accent");
				}
				else
				{
					button.Click += (_, _) => SongbookChosen?.Invoke(songbook);
				}

				ToolTip.SetTip(button, songbook.IsPrivate ? "Your private songbook" : $"{songbook.Name}'s songbook");
				chooser.Children.Add(button);
			}

			DockPanel.SetDock(chooser, Dock.Top);
			return chooser;
		}

		/// <summary>
		/// The songbook's tags as small buttons below the search box (see <see cref="TagFilterButton"/>):
		/// clicked, a tag narrows the list to the songs that have it; longclicked, to those that don't.
		/// Hidden when no song has tags.
		/// </summary>
		private static Control TagFilter(IReadOnlyList<string> tags, HashSet<string> chosen, HashSet<string> without, Action changed)
		{
			var filter = new WrapPanel
			{
				Orientation = Avalonia.Layout.Orientation.Horizontal,
				ItemSpacing = 6,
				LineSpacing = 6,
				Margin = new Avalonia.Thickness(0, -6, 0, 16),
				IsVisible = tags.Count > 0,
			};
			foreach (var tag in tags)
			{
				var state = without.Contains(tag) ? TagFilterState.Without : chosen.Contains(tag) ? TagFilterState.With : TagFilterState.Off;
				filter.Children.Add(TagFilterButton.Create(tag, state, 13, new Avalonia.Thickness(10, 2), next =>
				{
					chosen.Remove(tag);
					without.Remove(tag);
					if (next == TagFilterState.With)
					{
						chosen.Add(tag);
					}
					else if (next == TagFilterState.Without)
					{
						without.Add(tag);
					}

					changed();
				}));
			}

			DockPanel.SetDock(filter, Dock.Top);
			return filter;
		}

		/// <summary>
		/// A song's row as the songbook shows it: its name, and at the right its tags, then its key, tempo
		/// and beat at the right, after <paramref name="note"/> (e.g. "in setlist") when there is one;
		/// the current song has a check at the very right.
		/// </summary>
		internal static Control SongRow(SongDetails song, string? note, bool isCurrent = false)
		{
			// At the right the key, tempo and beat, and before them the tags: docked, they keep their
			// room, and the name (last, filling the rest) is trimmed with "…" when it doesn't fit.
			var row = new DockPanel();
			if (isCurrent)
			{
				row.Children.Add(PageLayout.CurrentCheck("The current song"));
			}

			var info = PageLayout.SongInfoText(song, note);
			info.Margin = new Avalonia.Thickness(12, 0, 0, 0);
			DockPanel.SetDock(info, Dock.Right);
			row.Children.Add(info);
			var tags = SongTags.Sorted(song.Tags);
			if (tags.Count > 0)
			{
				var chips = PageLayout.TagChips(tags);
				chips.Margin = new Avalonia.Thickness(10, 0, 0, 0);
				chips.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
				DockPanel.SetDock(chips, Dock.Right);
				row.Children.Add(chips);
			}

			row.Children.Add(new TextBlock { Text = song.Name, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
			return row;
		}

		/// <summary>
		/// The songs as a selectable list: a click selects (toolbar: make current, add to a setlist,
		/// edit, delete), a double click opens. The current song has a check in its row instead of the
		/// make-current button (it's current already).
		/// </summary>
		private SelectableList<SongDetails> SongList(IReadOnlyList<SongDetails> songs, Guid? currentSongId, string emptyText)
		{
			checkBoxes.Clear();
			var list = new SelectableList<SongDetails>(
				songs,
				song => CheckableRow(song, SongRow(song, null, song.Id == currentSongId)),
				song => song.Id == currentSongId,
				song => SongTools(song, song.Id == currentSongId),
				song => SongSelected?.Invoke(song),
				emptyText);
			DimCheckBoxes();
			return list;
		}

		/// <summary>A song's row with a check box at its left, to choose several songs for the setlist tool.</summary>
		private Control CheckableRow(SongDetails song, Control row)
		{
			var box = new CheckBox
			{
				IsChecked = checkedSongs.Contains(song.Id),
				MinWidth = 0,
				Padding = new Avalonia.Thickness(0),
				// 6px into the row's left padding, so the check box sits nearer the edge.
				Margin = new Avalonia.Thickness(-6, -6, 10, -6),
				VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
			};
			ToolTip.SetTip(box, $"Choose {song.Name} for a setlist");
			box.IsCheckedChanged += (_, _) =>
			{
				if (box.IsChecked == true)
				{
					checkedSongs.Add(song.Id);
				}
				else
				{
					checkedSongs.Remove(song.Id);
				}

				DimCheckBoxes();
			};
			checkBoxes.Add(box);
			DockPanel.SetDock(box, Dock.Left);
			return new DockPanel { Children = { box, row } };
		}

		/// <summary>The check boxes dim grey while none is checked, so they don't take the eye; clear once one is.</summary>
		private void DimCheckBoxes()
		{
			var anyChecked = AnyChecked();
			foreach (var box in checkBoxes)
			{
				box.Opacity = anyChecked ? 1 : 0.35;
			}
		}

		private IEnumerable<Control> SongTools(SongDetails song, bool isCurrent)
		{
			if (!isCurrent)
			{
				yield return PageLayout.OpenButton($"Open {song.Name}", () => SongSelected?.Invoke(song));
			}

			yield return AddToSetlistButton(song);
			yield return PageLayout.EditButton($"Edit {song.Name}", () => SongEditRequested?.Invoke(song));
			yield return PageLayout.DeleteButton($"Delete '{song.Name}'", $"Delete '{song.Name}'? It's also removed from every setlist it's in.", () => SongDeleteRequested?.Invoke(song));
		}

		/// <summary>
		/// A toolbar button that adds the song to a setlist: it opens a menu of the band's setlists
		/// (loaded when it's clicked, so they're the ones there are now) to choose the one.
		/// </summary>
		private Button AddToSetlistButton(SongDetails song)
		{
			var button = HomeView.IconButton(AddToSetlistIcon, $"Add {song.Name} to a setlist");
			button.Click += async (_, _) =>
			{
				if (LoadSetlists is not { } load)
				{
					return;
				}

				IReadOnlyList<SetlistSummary> setlists;
				button.IsEnabled = false;
				try
				{
					setlists = await load();
				}
				catch (Exception ex)
				{
					ShowStatus($"Couldn't get the setlists. {ex.Message}");
					return;
				}
				finally
				{
					button.IsEnabled = true;
				}

				var menu = new MenuFlyout();
				if (setlists.Count == 0)
				{
					menu.Items.Add(new MenuItem { Header = "There are no setlists yet", IsEnabled = false });
				}

				foreach (var setlist in setlists)
				{
					var item = new MenuItem { Header = setlist.Name };
					item.Click += (_, _) => SongAddToSetlistRequested?.Invoke(song, setlist);
					menu.Items.Add(item);
				}

				menu.ShowAt(button);
			};
			return button;
		}

		/// <summary>Whether the song's name, artist, tags or lyrics contain the text (ignoring case; chords in the lyrics don't get in the way).</summary>
		private static bool Matches(SongDetails song, string text)
			=> song.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
			   || song.Tags.Contains(text, StringComparison.CurrentCultureIgnoreCase)
			   || song.Artist.Contains(text, StringComparison.CurrentCultureIgnoreCase)
			   || ChordPro.StripChords(song.Lyrics).Contains(text, StringComparison.CurrentCultureIgnoreCase);

		/// <summary>Imports a song from a text or ChordPro file the user picks.</summary>
		private async Task ImportAsync()
		{
			try
			{
				if (await SongFiles.PickAsync(this) is { } song)
				{
					SongImported?.Invoke(song.Name, song.Lyrics);
				}
			}
			catch (InvalidOperationException ex)
			{
				ShowStatus(ex.Message);
			}
		}

		/// <summary>A full-width, plain button in a popup, looking like a menu item.</summary>
		private static Button PopupItem(string text, string? icon = null) => new()
		{
			// The icon (if any) before the text, as in a menu.
			Content = icon == null
				? text
				: new StackPanel
				{
					Orientation = Avalonia.Layout.Orientation.Horizontal,
					Spacing = 10,
					Children = { LineIcon(icon, 18), new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
				},
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
			HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
			Background = Brushes.Transparent,
			BorderThickness = new Avalonia.Thickness(0),
			Padding = new Avalonia.Thickness(10, 7),
		};

		/// <summary>A line icon (24×24 path data) drawn in the text colour around it, at the size given.</summary>
		private static Avalonia.Controls.Shapes.Path LineIcon(string data, double size)
		{
			var icon = new Avalonia.Controls.Shapes.Path
			{
				Data = Geometry.Parse(data),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Stretch = Stretch.Uniform,
				Width = size,
				Height = size,
				VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
			};
			icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, icon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			return icon;
		}

		// The setlist popup's icons: play (a triangle), a new setlist (a list with a plus), delete (a
		// trash can); and the magnifying glass in the search box.
		private const string PlayIcon = "M7,4.5 L19,12 L7,19.5 Z";
		private const string NewSetlistIcon = "M4,7 H15 M4,12 H12 M4,17 H10 M17,13 V21 M13,17 H21";
		private const string TrashIcon = "M5,7 H19 M10,7 V5 H14 V7 M7,7 L8,20 H16 L17,7 M10,11 V16 M14,11 V16";
		// An empty check box (a rounded square), for clearing the checks.
		private const string UncheckedIcon = "M7,4 H17 A3,3 0 0 1 20,7 V17 A3,3 0 0 1 17,20 H7 A3,3 0 0 1 4,17 V7 A3,3 0 0 1 7,4 Z";
		private const string SearchIcon = "M4,10.5 A6.5,6.5 0 1 0 17,10.5 A6.5,6.5 0 1 0 4,10.5 M15.2,15.2 L20,20";

		private void ShowStatus(string text)
		{
			status.Text = text;
			status.IsVisible = true;
		}
	}
}
