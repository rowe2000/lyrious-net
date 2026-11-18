using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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

		/// <summary>The band whose songbook this is.</summary>
		public GroupSummary Group { get; }

		/// <summary>The song shown as the current one (when the page was made).</summary>
		public Guid? CurrentSongId { get; }

		public SongsView(GroupSummary group, IReadOnlyList<SongDetails> songs, Guid? currentSongId)
		{
			Group = group;
			CurrentSongId = currentSongId;
			// The list, rebuilt as the search text changes.
			var list = new ContentControl();
			var search = new TextBox { PlaceholderText = "Search songs and lyrics", Margin = new Avalonia.Thickness(0, 0, 0, 16) };
			DockPanel.SetDock(search, Dock.Top);

			void ShowList()
			{
				var text = search.Text?.Trim() ?? "";
				var shown = text.Length == 0 ? songs : songs.Where(song => Matches(song, text)).ToArray();
				list.Content = SongList(shown, currentSongId, songs.Count == 0
					? "There are no songs in the songbook yet. Add one with +."
					: $"No songs match \"{text}\".");
			}

			search.TextChanged += (_, _) => ShowList();
			ShowList();

			var (nameRow, showNameRow) = PageLayout.NameRow("Name of the new song", name => EmptySongRequested?.Invoke(name));
			DockPanel.SetDock(status, Dock.Top);

			var addButton = PageLayout.AddButton("Add song");
			var empty = new MenuItem { Header = "New empty song…" };
			empty.Click += (_, _) => showNameRow();
			var import = new MenuItem { Header = "Import from file…" };
			import.Click += (_, _) => _ = ImportAsync();
			var internet = new MenuItem { Header = "Find on the internet…" };
			internet.Click += (_, _) => InternetSearchRequested?.Invoke();
			addButton.Flyout = new MenuFlyout { Items = { empty, import, internet } };

			Content = PageLayout.Page(
				group.Name,
				new DockPanel { Children = { nameRow, status, search, list } },
				subtitle: "Songbook · choose a song",
				titleAction: addButton,
				closable: true);
		}

		/// <summary>The songs as a selectable list: a click selects (toolbar: open, edit, delete), a double click opens.</summary>
		private SelectableList<SongDetails> SongList(IReadOnlyList<SongDetails> songs, Guid? currentSongId, string emptyText)
			=> new(
				songs,
				song => PageLayout.RowContent(song.Name, PageLayout.SongInfo(song)),
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

		/// <summary>Whether the song's name or lyrics contain the text (ignoring case; chords in the lyrics don't get in the way).</summary>
		private static bool Matches(SongDetails song, string text)
			=> song.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
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
