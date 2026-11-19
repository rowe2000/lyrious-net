using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Finds a song on the internet (an online lyrics database): search by title and/or artist,
	/// pick a result to read its lyrics, then add it to the band's songbook. The results are kept
	/// while previewing, so going back doesn't search again. The same search can also be opened on
	/// Ultimate Guitar in the browser: it has no API and doesn't allow automated searches, so its
	/// results can't be shown here.
	/// </summary>
	public sealed class InternetSongView : UserControl
	{
		private readonly Func<string, Task<IReadOnlyList<InternetSong>>> search;
		private readonly IReadOnlySet<string> songbookNames;
		private readonly TextBox query = PageLayout.WithClearButton(new TextBox { PlaceholderText = "Title and/or artist" });
		private readonly Button searchButton = new() { Content = "Search", IsEnabled = false };
		private readonly Button ultimateGuitarButton = new() { Content = "Search on Ultimate Guitar ↗", IsEnabled = false, Margin = new Thickness(0, 0, 0, 16) };
		private readonly ContentControl results = new();
		private readonly Control searchPage;
		private IReadOnlyList<InternetSong> lastResults = [];
		private readonly string addText;

		/// <summary>Raised when the user wants the chosen song added to the songbook.</summary>
		public event Action<InternetSong>? AddRequested;

		/// <param name="search">Searches the online database; throws if it can't be reached.</param>
		/// <param name="songbookNames">Names already in the songbook (any case), to mark results the band has.</param>
		/// <param name="addText">The text of the add button in the preview, e.g. "Add to songbook".</param>
		public InternetSongView(Func<string, Task<IReadOnlyList<InternetSong>>> search, IReadOnlySet<string> songbookNames, string addText = "Add to songbook")
		{
			this.addText = addText;
			this.search = search;
			this.songbookNames = songbookNames;

			searchButton.Classes.Add("accent");
			searchButton.Click += (_, _) => _ = SearchAsync();
			query.TextChanged += (_, _) => searchButton.IsEnabled = ultimateGuitarButton.IsEnabled = !string.IsNullOrWhiteSpace(query.Text);
			ToolTip.SetTip(ultimateGuitarButton, "Opens the search in your browser, to copy chords or lyrics into a song");
			ultimateGuitarButton.Click += (_, _) => _ = OpenUltimateGuitarAsync();
			DockPanel.SetDock(ultimateGuitarButton, Dock.Top);
			query.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Enter)
				{
					e.Handled = true;
					_ = SearchAsync();
				}
			};

			DockPanel.SetDock(searchButton, Dock.Right);
			searchButton.Margin = new Thickness(8, 0, 0, 0);
			var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16), Children = { searchButton, query } };
			DockPanel.SetDock(searchRow, Dock.Top);

			results.Content = Message("Search for a song to add its lyrics to the songbook.");
			searchPage = PageLayout.Page(
				"Find song online",
				new DockPanel { Children = { searchRow, ultimateGuitarButton, results } },
				subtitle: "Lyrics from LRCLIB (lrclib.net)",
				closable: true);
			Content = searchPage;
		}

		protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
		{
			base.OnLoaded(e);
			query.Focus();
		}

		private async Task SearchAsync()
		{
			var text = query.Text?.Trim();
			if (string.IsNullOrEmpty(text) || !searchButton.IsEnabled)
			{
				return;
			}

			searchButton.IsEnabled = false;
			results.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			try
			{
				lastResults = await search(text);
				ShowResults();
			}
			catch (Exception ex)
			{
				results.Content = Message($"Couldn't search. {ex.Message}");
			}
			finally
			{
				searchButton.IsEnabled = !string.IsNullOrWhiteSpace(query.Text);
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

			var uri = new Uri($"https://www.ultimate-guitar.com/search.php?search_type=title&value={Uri.EscapeDataString(text)}");
			if (!await topLevel.Launcher.LaunchUriAsync(uri))
			{
				results.Content = Message($"Couldn't open the browser. The address is {uri}");
			}
		}

		private void ShowResults()
		{
			var rows = lastResults.Select(song => (Control)ResultRow(song)).ToArray();
			results.Content = PageLayout.List(rows, "No songs found. Try other words, or the artist's name too.");
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
				notes.Add(song.Instrumental ? "instrumental" : "no lyrics");
			}

			if (songbookNames.Contains(song.Title))
			{
				notes.Add("in songbook");
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
			add.Click += (_, _) => AddRequested?.Invoke(song);
			DockPanel.SetDock(add, Dock.Top);

			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);
			var lyrics = new TextBlock
			{
				Text = hasLyrics ? song.Lyrics : "This song has no lyrics in the database. It can still be added, and the lyrics typed in later.",
				FontSize = hasLyrics ? 20 : 16,
				LineHeight = hasLyrics ? 30 : double.NaN,
				Opacity = hasLyrics ? 1 : 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 0, 0, 48),
			};

			var subtitle = songbookNames.Contains(song.Title) ? $"{song.Artist} · a song with this name is in the songbook already" : song.Artist;
			Content = PageLayout.Page(
				song.Title,
				new DockPanel { Children = { add, new ScrollViewer { Content = lyrics } } },
				backText: "‹ Search results",
				back: () => Content = searchPage,
				subtitle: subtitle,
				closable: true);
		}

		private static TextBlock Message(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
	}
}
