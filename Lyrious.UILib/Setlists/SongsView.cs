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

		// A list (three lines) with a play triangle at its lower right.
		private const string AsSetlistIcon = "M4,6 H20 M4,11 H14 M4,16 H10 M15,14 L21,17.5 L15,21 Z";

		/// <summary>The band whose songbook this is.</summary>
		public GroupSummary Group { get; }

		/// <summary>The song shown as the current one (when the page was made).</summary>
		public Guid? CurrentSongId { get; }

		/// <summary>Raised when the user chooses another songbook (the private one or the joined band's).</summary>
		public event Action<GroupSummary>? SongbookChosen;

		/// <param name="songbooks">The songbooks to choose between (e.g. the private one and the joined band's), shown as buttons above the list; none hides them.</param>
		public SongsView(GroupSummary group, IReadOnlyList<SongDetails> songs, Guid? currentSongId, IReadOnlyList<GroupSummary>? songbooks = null)
		{
			Group = group;
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

			// The songs listed (as searched and filtered) as the setlist in the right drawer, not saved.
			var asSetlistButton = HomeView.IconButton(AsSetlistIcon, "Show these songs as the setlist in the right drawer (not saved as a setlist)");
			asSetlistButton.IsEnabled = songs.Count > 0;
			asSetlistButton.Click += (_, _) =>
			{
				if (shownSongs.Count > 0)
				{
					ShowAsSetlistRequested?.Invoke(shownSongs);
				}
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
				new DockPanel { Children = { SongbookChooser(group, songbooks ?? []), nameRow, status, search, tagFilter, list } },
				subtitle: $"{group.Name} · choose a song",
				titleAction: new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Children = { asSetlistButton, addButton } },
				closable: true);
		}

		/// <summary>
		/// The songbooks to choose between as buttons flowing left to right, as wide as their names;
		/// the shown one is highlighted. Hidden when there's nothing to choose between.
		/// </summary>
		private Control SongbookChooser(GroupSummary shown, IReadOnlyList<GroupSummary> songbooks)
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
				var button = new Button
				{
					Content = new TextBlock { Text = songbook.Name, FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
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

		/// <summary>A song's row: its name, and its tags, key, tempo and beat at the right.</summary>
		private static Control SongRow(SongDetails song) => SongRow(song, null);

		/// <summary>
		/// A song's row as the songbook shows it: its name, and at the right its tags, then its key, tempo
		/// and beat at the right, after <paramref name="note"/> (e.g. "in setlist") when there is one.
		/// </summary>
		internal static Control SongRow(SongDetails song, string? note)
		{
			// At the right the key, tempo and beat, and before them the tags: docked, they keep their
			// room, and the name (last, filling the rest) is trimmed with "…" when it doesn't fit.
			var row = new DockPanel();
			var info = PageLayout.SongInfoText(song, note);
			info.Margin = new Avalonia.Thickness(12, 0, 0, 0);
			DockPanel.SetDock(info, Dock.Right);
			row.Children.Add(info);
			var tags = SongTags.Parse(song.Tags);
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

		/// <summary>The songs as a selectable list: a click selects (toolbar: open, edit, delete), a double click opens.</summary>
		private SelectableList<SongDetails> SongList(IReadOnlyList<SongDetails> songs, Guid? currentSongId, string emptyText)
			=> new(
				songs,
				SongRow,
				song => song.Id == currentSongId,
				song =>
				[
					PageLayout.OpenButton($"Open {song.Name}", () => SongSelected?.Invoke(song)),
					AddToSetlistButton(song),
					PageLayout.EditButton($"Edit {song.Name}", () => SongEditRequested?.Invoke(song)),
					PageLayout.DeleteButton($"Delete {song.Name}", $"Delete \"{song.Name}\"? It's also removed from every setlist it's in.", () => SongDeleteRequested?.Invoke(song)),
				],
				song => SongSelected?.Invoke(song),
				emptyText);

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
