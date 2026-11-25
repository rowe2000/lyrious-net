using System;
using System.Collections.Generic;
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
	/// <summary>
	/// Finds a song online, by title and/or artist, on the source chosen: Ultimate Guitar (the default),
	/// whose chords and tabs show in the lyrics view, just for the user until added; or LRCLIB, a lyrics
	/// database, whose lyrics show as text. A result picked can be added to the band's songbook. The
	/// results are kept while previewing, so going back doesn't search again. In a browser, which can't
	/// read another site, an Ultimate Guitar search opens there instead.
	/// </summary>
	public sealed class InternetSongView : UserControl
	{
		private readonly Func<string, Task<IReadOnlyList<InternetSong>>> search;
		// Searches Ultimate Guitar; null where it can't be (a browser): the search opens there instead.
		private readonly Func<string, Task<IReadOnlyList<UltimateGuitarSong>>>? searchUltimateGuitar;
		// Reads a song's chords or tab from its page on Ultimate Guitar (with the search).
		private readonly Func<Uri, Task<UltimateGuitarTab>>? getUltimateGuitarTab;
		private readonly IReadOnlySet<string> songbookNames;
		private readonly TextBox query = PageLayout.WithClearButton(new TextBox { PlaceholderText = L.T("Title and/or artist") });
		// In the box, at its right end after the ✕ (see the constructor).
		private readonly Button searchButton = HomeView.IconButton(AppIcons.Search, L.T("Search"));
		// Where to search: Ultimate Guitar (chords and tabs) or LRCLIB (lyrics).
		private readonly RadioButton ultimateGuitarSource = new() { Content = "Ultimate Guitar", GroupName = "find-online-source" };
		private readonly RadioButton lrclibSource = new() { Content = "LRCLIB", GroupName = "find-online-source" };
		// The source chosen, while the app runs: Ultimate Guitar unless LRCLIB was chosen.
		private static bool lrclibChosen;
		// The search, kept while the app runs until a song found is added (see ForgetSearch), so coming
		// back to the page shows it as it was: whether something was searched (changing the source then
		// searches it there too), the words, each source's last results, and which of them are listed.
		private static bool searched;
		// (The words as searched, not as typed since: Escape in the box puts back its text from when it got
		// the focus, e.g. when leaving the page with Escape.)
		private static string keptQuery = "";
		private static bool? listedFromLrclib;
		private readonly ContentControl results = new();
		private readonly Control searchPage;
		private static IReadOnlyList<InternetSong> lastResults = [];
		private static IReadOnlyList<UltimateGuitarSong> lastUltimateGuitarResults = [];
		// Counts the songs shown from Ultimate Guitar, so one that's still loading when the next is
		// asked for doesn't show over it.
		private int ultimateGuitarShown;
		private readonly string addText;

		/// <summary>Raised when the user wants the chosen song added to the songbook.</summary>
		public event Action<InternetSong>? AddRequested;

		/// <param name="search">Searches the online database; throws if it can't be reached.</param>
		/// <param name="songbookNames">Names already in the songbook (any case), to mark results the band has.</param>
		/// <param name="addText">The text of the add button in the preview, e.g. "Add to songbook".</param>
		/// <param name="searchUltimateGuitar">Searches Ultimate Guitar; null where it can't be (the search then opens in the browser).</param>
		/// <param name="getUltimateGuitarTab">Reads a found song's chords or tab from Ultimate Guitar, to show it.</param>
		public InternetSongView(Func<string, Task<IReadOnlyList<InternetSong>>> search, IReadOnlySet<string> songbookNames, string addText = "Add to songbook",
			Func<string, Task<IReadOnlyList<UltimateGuitarSong>>>? searchUltimateGuitar = null, Func<Uri, Task<UltimateGuitarTab>>? getUltimateGuitarTab = null)
		{
			this.addText = addText;
			this.search = search;
			this.searchUltimateGuitar = searchUltimateGuitar;
			this.getUltimateGuitarTab = getUltimateGuitarTab;
			this.songbookNames = songbookNames;

			// The search button in the box, at its right end after the ✕ (which clears the box).
			searchButton.Classes.Add("accent");
			searchButton.IsEnabled = false;
			PageLayout.FitInBox(query, searchButton);
			var clear = (Button)query.InnerRightContent!;
			// Not at the end any more: the search button is.
			PageLayout.FitInBox(query, clear, atEnd: false, iconSize: 14);
			query.InnerRightContent = new StackPanel
			{
				Orientation = Avalonia.Layout.Orientation.Horizontal,
				Children = { clear, searchButton },
			};
			searchButton.Click += (_, _) => _ = SearchAsync();
			query.TextChanged += (_, _) => searchButton.IsEnabled = !string.IsNullOrWhiteSpace(query.Text);

			// The source: Ultimate Guitar's chords and tabs, or LRCLIB's lyrics.
			ToolTip.SetTip(ultimateGuitarSource, searchUltimateGuitar != null
				? L.T("Chords and tabs from Ultimate Guitar: pick one to see it here with its chords, only for you, and add it if you like")
				: L.T("Opens the search on Ultimate Guitar in your browser, to copy chords or lyrics into a song"));
			ToolTip.SetTip(lrclibSource, L.T("Lyrics (without chords) from LRCLIB, a free lyrics database (lrclib.net)"));
			ultimateGuitarSource.IsChecked = !lrclibChosen;
			lrclibSource.IsChecked = lrclibChosen;
			lrclibSource.IsCheckedChanged += (_, _) =>
			{
				lrclibChosen = lrclibSource.IsChecked == true;
				// What was searched, searched again in the source chosen.
				if (searched)
				{
					_ = SearchAsync();
				}
			};
			var sources = new StackPanel
			{
				Orientation = Avalonia.Layout.Orientation.Horizontal,
				Spacing = 16,
				Margin = new Thickness(0, 0, 0, 16),
				Children = { ultimateGuitarSource, lrclibSource },
			};
			DockPanel.SetDock(sources, Dock.Top);
			query.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Enter)
				{
					e.Handled = true;
					_ = SearchAsync();
				}
			};

			query.Margin = new Thickness(0, 0, 0, 16);
			DockPanel.SetDock(query, Dock.Top);

			results.Content = Message(L.T("Search for a song: its chords on Ultimate Guitar, or its lyrics on LRCLIB."));
			searchPage = PageLayout.Page(
				L.T("Find online"),
				new DockPanel { Children = { query, sources, results } },
				closable: true,
				icon: AppIcons.FindOnline);
			Content = searchPage;

			// The search as it was left (see keptQuery).
			query.Text = keptQuery;
			switch (listedFromLrclib)
			{
				case true:
					ShowResults();
					break;
				case false:
					ShowUltimateGuitarResults();
					break;
			}
		}

		/// <summary>Forgets the search kept for coming back to the page: a song found was added, so the next search starts afresh.</summary>
		private static void ForgetSearch()
		{
			searched = false;
			keptQuery = "";
			listedFromLrclib = null;
			lastResults = [];
			lastUltimateGuitarResults = [];
		}

		/// <summary>Adds the song found (see <see cref="AddRequested"/>); the search is forgotten then.</summary>
		private void Add(InternetSong song)
		{
			ForgetSearch();
			AddRequested?.Invoke(song);
		}

		protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
		{
			base.OnLoaded(e);
			query.Focus();
		}

		/// <summary>The bar that shows a search is under way: just below the source buttons, at the top of the results' room.</summary>
		private static ProgressBar SearchProgress() => new() { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Top };

		/// <summary>Searches the source chosen: Ultimate Guitar (in the browser where it can't be searched here) or LRCLIB.</summary>
		private Task SearchAsync()
		{
			if (lrclibChosen)
			{
				return SearchLrclibAsync();
			}

			return searchUltimateGuitar != null ? SearchUltimateGuitarAsync() : OpenUltimateGuitarAsync();
		}

		/// <summary>Searches LRCLIB and lists what it found (see <see cref="ResultRow"/>).</summary>
		private async Task SearchLrclibAsync()
		{
			var text = query.Text?.Trim();
			if (string.IsNullOrEmpty(text) || !searchButton.IsEnabled)
			{
				return;
			}

			searched = true;
			keptQuery = text;
			searchButton.IsEnabled = false;
			results.Content = SearchProgress();
			try
			{
				lastResults = await search(text);
				listedFromLrclib = true;
				ShowResults();
			}
			catch (Exception ex)
			{
				listedFromLrclib = null;
				results.Content = Message(L.T("Couldn't search. {0}", ex.Message));
			}
			finally
			{
				searchButton.IsEnabled = !string.IsNullOrWhiteSpace(query.Text);
			}
		}

		/// <summary>Searches Ultimate Guitar and lists what it found (see <see cref="UltimateGuitarRow"/>).</summary>
		private async Task SearchUltimateGuitarAsync()
		{
			var text = query.Text?.Trim();
			if (string.IsNullOrEmpty(text) || searchUltimateGuitar == null || !searchButton.IsEnabled)
			{
				return;
			}

			searched = true;
			keptQuery = text;
			searchButton.IsEnabled = false;
			results.Content = SearchProgress();
			try
			{
				lastUltimateGuitarResults = await searchUltimateGuitar(text);
				listedFromLrclib = false;
				ShowUltimateGuitarResults();
			}
			catch (Exception ex)
			{
				listedFromLrclib = null;
				results.Content = Message(L.T("Couldn't search Ultimate Guitar. {0}", ex.Message));
			}
			finally
			{
				searchButton.IsEnabled = !string.IsNullOrWhiteSpace(query.Text);
			}
		}

		/// <summary>Lists Ultimate Guitar's last results (see <see cref="UltimateGuitarRow"/>).</summary>
		private void ShowUltimateGuitarResults()
		{
			var rows = lastUltimateGuitarResults.Select(song => (Control)UltimateGuitarRow(song)).ToArray();
			results.Content = PageLayout.List(rows, L.T("Ultimate Guitar found no chords or tabs. Try other words, or the artist's name too."));
		}

		/// <summary>One of Ultimate Guitar's results: title, then artist, what it is and its rating; it shows the song (see <see cref="ShowUltimateGuitarTabAsync"/>).</summary>
		private Button UltimateGuitarRow(UltimateGuitarSong song)
		{
			var details = new List<string> { song.Artist, song.Type };
			if (song.Rating is { } rating)
			{
				details.Add(L.T("★ {0} ({1})", rating.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), song.Votes));
			}

			var content = new StackPanel { Spacing = 2 };
			content.Children.Add(new TextBlock { Text = song.Title, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis });
			content.Children.Add(new TextBlock { Text = string.Join(" · ", details.Where(d => d.Length > 0)), FontSize = 13, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });
			if (songbookNames.Contains(song.Title))
			{
				content.Children.Add(new TextBlock { Text = L.T("in songbook"), FontSize = 13, Opacity = 0.6 });
			}

			var row = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 10),
			};
			row.Click += (_, _) => _ = ShowUltimateGuitarTabAsync(song);
			return row;
		}

		/// <summary>
		/// The found song's chords or tab, read from Ultimate Guitar and made into Lyrious' lyrics (see
		/// <see cref="UltimateGuitarText"/>), in the lyrics view as any song: only for the user, not saved
		/// until the add button adds it (as it shows, with changes made in its editor) to the songbook, and
		/// the setlist when searching from one (see <see cref="AddRequested"/>). Back returns to the results.
		/// </summary>
		private async Task ShowUltimateGuitarTabAsync(UltimateGuitarSong found)
		{
			if (getUltimateGuitarTab == null)
			{
				return;
			}

			// The results before and after it, if there are.
			var at = IndexOf(lastUltimateGuitarResults, found);
			var before = at > 0 ? lastUltimateGuitarResults[at - 1] : null;
			var after = at >= 0 && at + 1 < lastUltimateGuitarResults.Count ? lastUltimateGuitarResults[at + 1] : null;
			Control Page(Control content, string? subtitle) => PageLayout.Page(
				found.Title,
				content,
				backText: L.T("Search results"),
				back: () => Content = searchPage,
				subtitle: subtitle,
				closable: true,
				previous: before != null ? () => _ = ShowUltimateGuitarTabAsync(before) : null,
				next: after != null ? () => _ = ShowUltimateGuitarTabAsync(after) : null,
				place: at >= 0 ? (at + 1, lastUltimateGuitarResults.Count) : null);

			var showing = ++ultimateGuitarShown;
			Content = Page(new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8), VerticalAlignment = VerticalAlignment.Top }, found.Artist);
			try
			{
				var tab = await getUltimateGuitarTab(found.Link);
				if (showing != ultimateGuitarShown)
				{
					return; // Another was asked for meanwhile.
				}

				var name = tab.Title.Length > 0 ? tab.Title : found.Title;
				var artist = tab.Artist.Length > 0 ? tab.Artist : found.Artist;
				var song = new SongDetails(Guid.NewGuid(), name, tab.Key, 120, "4/4", UltimateGuitarText.ToChordPro(tab.Content, tab.Capo), OriginalKey: tab.Key, Artist: artist);
				// Changes in its editor only change what shows here (and what the add button adds).
				var shown = song;
				// Only the lyrics, from the top: no toolbar (editing, transpose, media) on a song that isn't the band's yet.
				var lyrics = new LyricScrollView(song, edited =>
				{
					shown = edited;
					return Task.FromResult(edited);
				})
				{
					WithoutToolbar = true,
				};

				var add = new Button { Content = addText, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 10), Margin = new Thickness(0, 0, 0, 16) };
				add.Classes.Add("accent");
				add.Click += (_, _) =>
				{
					// Once: the page changes to the song added.
					add.IsEnabled = false;
					Add(new InternetSong(shown.Name, shown.Artist, "", TimeSpan.Zero, shown.Lyrics, Instrumental: false, Key: shown.OriginalKey));
				};
				DockPanel.SetDock(add, Dock.Top);
				Content = Page(new DockPanel { Children = { add, PageLayout.WholeWidth(lyrics) } }, artist.Length > 0 ? L.T("{0} · only for you, not saved", artist) : L.T("Only for you, not saved"));
			}
			catch (Exception ex) when (showing == ultimateGuitarShown)
			{
				Content = Page(Message(L.T("Couldn't read the song from Ultimate Guitar. {0}", ex.Message)), found.Artist);
			}
			catch (Exception ex)
			{
				// Another was asked for meanwhile: this one's failure doesn't matter any more.
				System.Diagnostics.Debug.WriteLine($"Couldn't read a song from Ultimate Guitar: {ex.Message}");
			}
		}

		/// <summary>Opens the search on Ultimate Guitar in the browser (a new tab in the web app).</summary>
		private async Task OpenUltimateGuitarAsync()
		{
			var text = query.Text?.Trim();
			if (string.IsNullOrEmpty(text) || TopLevel.GetTopLevel(this) is not { } topLevel)
			{
				return;
			}

			keptQuery = text;
			var uri = new Uri($"https://www.ultimate-guitar.com/search.php?search_type=title&value={Uri.EscapeDataString(text)}");
			if (!await topLevel.Launcher.LaunchUriAsync(uri))
			{
				results.Content = Message(L.T("Couldn't open the browser. The address is {0}", uri));
			}
		}

		private void ShowResults()
		{
			var rows = lastResults.Select(song => (Control)ResultRow(song)).ToArray();
			results.Content = PageLayout.List(rows, L.T("No songs found. Try other words, or the artist's name too."));
		}

		/// <summary>One result: title, then artist, album and length, and whether the band has it already.</summary>
		private Button ResultRow(InternetSong song)
		{
			var details = new List<string> { song.Artist };
			if (!string.IsNullOrWhiteSpace(song.Album))
			{
				details.Add(song.Album);
			}

			if (song.Duration > TimeSpan.Zero)
			{
				details.Add(song.Duration.ToString(song.Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"));
			}

			var notes = new List<string>();
			if (string.IsNullOrWhiteSpace(song.Lyrics))
			{
				notes.Add(song.Instrumental ? "instrumental" : L.T("no lyrics"));
			}

			if (songbookNames.Contains(song.Title))
			{
				notes.Add(L.T("in songbook"));
			}

			var content = new StackPanel { Spacing = 2 };
			content.Children.Add(new TextBlock { Text = song.Title, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis });
			content.Children.Add(new TextBlock { Text = string.Join(" · ", details.Where(d => d.Length > 0)), FontSize = 13, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });
			if (notes.Count > 0)
			{
				content.Children.Add(new TextBlock { Text = string.Join(" · ", notes), FontSize = 13, Opacity = 0.6 });
			}

			var row = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 10),
			};
			row.Click += (_, _) => ShowPreview(song);
			return row;
		}

		/// <summary>The result's lyrics, with the add button; back returns to the results.</summary>
		private void ShowPreview(InternetSong song)
		{
			var add = new Button { Content = addText, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 10), Margin = new Thickness(0, 0, 0, 16) };
			add.Classes.Add("accent");
			add.Click += (_, _) => Add(song);
			DockPanel.SetDock(add, Dock.Top);

			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);
			var lyrics = new TextBlock
			{
				Text = hasLyrics ? song.Lyrics : L.T("This song has no lyrics in the database. It can still be added, and the lyrics typed in later."),
				FontSize = hasLyrics ? 20 : 16,
				LineHeight = hasLyrics ? 30 : double.NaN,
				Opacity = hasLyrics ? 1 : 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 0, 0, 48),
			};

			var subtitle = songbookNames.Contains(song.Title) ? L.T("{0} · a song with this name is in the songbook already", song.Artist) : song.Artist;
			// The results before and after it, if there are.
			var at = IndexOf(lastResults, song);
			var before = at > 0 ? lastResults[at - 1] : null;
			var after = at >= 0 && at + 1 < lastResults.Count ? lastResults[at + 1] : null;
			Content = PageLayout.Page(
				song.Title,
				new DockPanel { Children = { add, new ScrollViewer { Content = lyrics } } },
				backText: L.T("Search results"),
				back: () => Content = searchPage,
				subtitle: subtitle,
				closable: true,
				previous: before != null ? () => ShowPreview(before) : null,
				next: after != null ? () => ShowPreview(after) : null,
				place: at >= 0 ? (at + 1, lastResults.Count) : null);
		}

		/// <summary>Where <paramref name="item"/> is among <paramref name="items"/> (the very one, not an equal one); -1 if it isn't.</summary>
		private static int IndexOf<T>(IReadOnlyList<T> items, T item) where T : class
		{
			for (var i = 0; i < items.Count; i++)
			{
				if (ReferenceEquals(items[i], item))
				{
					return i;
				}
			}

			return -1;
		}

		private static TextBlock Message(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
	}
}
