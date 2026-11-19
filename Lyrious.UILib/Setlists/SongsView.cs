using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
			// The tags chosen to filter by: a song is shown when it has all of them. Of the tags chosen
			// before, those this songbook has.
			var allTags = SongTags.AllOf(songs);
			var chosenTags = new HashSet<string>(allTags.Where(savedTags.Contains), StringComparer.CurrentCultureIgnoreCase);
			// The songs the list shows now, for the setlist tool.
			IReadOnlyList<SongDetails> shownSongs = songs;

			void ShowList()
			{
				var text = search.Text?.Trim() ?? "";
				var shown = (IReadOnlyList<SongDetails>)songs
					.Where(song => text.Length == 0 || Matches(song, text))
					.Where(song => chosenTags.All(tag => SongTags.Has(song, tag)))
					.ToArray();
				string emptyText;
				if (songs.Count == 0)
				{
					emptyText = "There are no songs in the songbook yet. Add one with +.";
				}
				else if (chosenTags.Count == 0)
				{
					emptyText = $"No songs match \"{text}\".";
				}
				else
				{
					var tags = string.Join(", ", chosenTags);
					emptyText = text.Length == 0
						? $"No songs have all of these tags: {tags}."
						: $"No songs with the tags {tags} match \"{text}\".";
				}

				shownSongs = shown;
				list.Content = SongList(shown, currentSongId, emptyText);

				// Kept for the next time the page shows. Chosen tags this songbook doesn't have stay
				// chosen (for a songbook that has them).
				savedSearch = search.Text ?? "";
				savedTags.ExceptWith(allTags);
				savedTags.UnionWith(chosenTags);
			}

			var tagFilter = TagFilter(allTags, chosenTags, ShowList);

			search.TextChanged += (_, _) => ShowList();
			ShowList();

			var (nameRow, showNameRow) = PageLayout.NameRow("Name of the new song", name => EmptySongRequested?.Invoke(name));
			DockPanel.SetDock(status, Dock.Top);

			// The songs the setlist tool uses: the checked ones (in the songbook's order), else all listed.
			IReadOnlyList<SongDetails> ChosenSongs()
				=> AnyChecked() ? songs.Where(song => checkedSongs.Contains(song.Id)).ToArray() : shownSongs;

			// Saving asks for the new setlist's name in a row at the top, as a new song's does.
			IReadOnlyList<SongDetails> toSave = [];
			// Once it's made: the row closes, and the songs in it are checked no more.
			var (setlistNameRow, showSetlistNameRow) = PageLayout.NameRow("Name of the new setlist", name =>
			{
				SaveAsSetlistRequested?.Invoke(name, toSave);
				checkedSongs.ExceptWith(toSave.Select(song => song.Id));
				foreach (var box in checkBoxes)
				{
					box.IsChecked = false;
				}

				DimCheckBoxes();
			}, closeOnAdd: true);

			// The setlist tool: a menu to show the songs as the setlist in the right drawer (not
			// saved), or to save them as a new setlist; and to clear the checks.
			var asSetlistButton = HomeView.IconButton(AsSetlistIcon, "Use these songs as a setlist");
			asSetlistButton.IsEnabled = songs.Count > 0;
			// (The menu is made on each click: a menu with nothing in it yet doesn't open.)
			asSetlistButton.Click += (_, _) =>
			{
				var setlistMenu = new MenuFlyout();
				var chosen = ChosenSongs();
				var which = AnyChecked()
					? $"the {chosen.Count} checked song{(chosen.Count == 1 ? "" : "s")}"
					: chosen.Count == songs.Count ? $"all {chosen.Count} song{(chosen.Count == 1 ? "" : "s")}" : $"the {chosen.Count} listed song{(chosen.Count == 1 ? "" : "s")}";
				// Shown as the setlist in the right drawer, not saved.
				var show = new MenuItem { Header = $"Play {which}", IsEnabled = chosen.Count > 0 };
				show.Click += (_, _) => ShowAsSetlistRequested?.Invoke(chosen);
				var save = new MenuItem { Header = $"New setlist of {which}", IsEnabled = chosen.Count > 0 };
				save.Click += (_, _) =>
				{
					toSave = chosen;
					showSetlistNameRow();
				};
				setlistMenu.Items.Add(show);
				setlistMenu.Items.Add(save);
				if (AnyChecked())
				{
					var clear = new MenuItem { Header = "Clear the checks" };
					clear.Click += (_, _) =>
					{
						checkedSongs.ExceptWith(songIds);
						foreach (var box in checkBoxes)
						{
							box.IsChecked = false;
						}

						DimCheckBoxes();
					};
					setlistMenu.Items.Add(new Separator());
					setlistMenu.Items.Add(clear);
				}

				setlistMenu.ShowAt(asSetlistButton);
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
				new DockPanel { Children = { SongbookChooser(group, songbooks ?? [], playingSongbookId), nameRow, setlistNameRow, status, search, tagFilter, list } },
				subtitle: $"{group.Name} · choose a song",
				titleAction: new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Children = { asSetlistButton, addButton } },
				closable: true);
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
		/// The songbook's tags as small toggle buttons below the search box: each one chosen narrows
		/// the list to the songs that have it (and all the others chosen). Hidden when no song has tags.
		/// </summary>
		private static Control TagFilter(IReadOnlyList<string> tags, HashSet<string> chosen, Action changed)
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
				var button = new ToggleButton
				{
					Content = new TextBlock { Text = tag, FontSize = 13 },
					Padding = new Avalonia.Thickness(10, 2),
					MinHeight = 0,
					CornerRadius = new Avalonia.CornerRadius(12),
					IsChecked = chosen.Contains(tag),
				};
				ToolTip.SetTip(button, $"Show only songs tagged {tag}");
				button.IsCheckedChanged += (_, _) =>
				{
					if (button.IsChecked == true)
					{
						chosen.Add(tag);
					}
					else
					{
						chosen.Remove(tag);
					}

					changed();
				};
				filter.Children.Add(button);
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
				Margin = new Avalonia.Thickness(0, -6, 10, -6),
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
			yield return PageLayout.DeleteButton($"Delete {song.Name}", $"Delete \"{song.Name}\"? It's also removed from every setlist it's in.", () => SongDeleteRequested?.Invoke(song));
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

		private void ShowStatus(string text)
		{
			status.Text = text;
			status.IsVisible = true;
		}
	}
}
