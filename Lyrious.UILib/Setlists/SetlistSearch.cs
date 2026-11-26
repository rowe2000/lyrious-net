using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Searching a setlist's songs on a setlist page, as in the right drawer: the magnifying glass
	/// among the setlist's tools shows a search box in place of the setlist's name (the other tools
	/// stay), and turns into a ✕ that ends the search. Only the songs with the text in their name or
	/// tags are listed meanwhile. Under it, the setlist's tags and media kinds narrow the list further:
	/// a tag clicked to the songs that have it, longclicked to those that don't (as in the songbook);
	/// a media kind (Spotify, YouTube, sound file, other link) likewise, to the songs that have (or don't have) one. Enter opens
	/// the first song found; Escape ends the search.
	/// </summary>
	internal sealed class SetlistSearch
	{
		// As tall as the setlist's name box (see PageLayout.EditableTitle), whose place it takes.
		private readonly TextBox box = PageLayout.WithClearButton(new TextBox { PlaceholderText = L.T("Search songs and tags"), Height = 40, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsVisible = false });
		private readonly Button glass;
		private readonly Button end;
		private SelectableList<SetlistEntry>? list;
		private IReadOnlyList<SetlistEntry> entries = [];
		private readonly HashSet<string> chosenTags = [];
		private readonly HashSet<string> withoutTags = [];
		private readonly HashSet<MediaSite> chosenMedia = [];
		private readonly HashSet<MediaSite> withoutMedia = [];
		private readonly WrapPanel filters = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, IsVisible = false };

		/// <summary>The tags and media kinds to narrow the list by, for under the setlist's name; shown only while searching (when there are any).</summary>
		public Control Filters => filters;

		/// <summary>The magnifying glass (✕ while searching), for the setlist's tools.</summary>
		public Control Tool { get; }

		/// <summary>The search box, to put in the setlist name's place; shown only while searching.</summary>
		public Control Box => box;

		/// <summary>Whether the search shows (the setlist's name is hidden meanwhile).</summary>
		public bool IsSearching => box.IsVisible;

		/// <summary>Called when the songs listed change (the search text or a filter changed), so the page's song count follows; one at a time (the page sets it again when it lists the songs anew).</summary>
		public Action? Filtered { get; set; }

		/// <summary>Raised when the search shows (true) or ends (false), so the page hides or shows the setlist's name.</summary>
		public event Action<bool>? SearchingChanged;

		/// <param name="open">Opens a song (Enter: the first one found).</param>
		public SetlistSearch(Action<SetlistEntry> open)
		{
			glass = HomeView.IconButton(AppIcons.Search, L.T("Search the setlist"));
			// While searching, Escape only ends the search (see the box's KeyDown too).
			end = EscapeKey.MarkAsClose(PageLayout.CancelButton(L.T("End the search")), before: true);
			end.IsVisible = false;
			Tool = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { glass, end } };
			box.Margin = new Avalonia.Thickness(0, 0, 8, 0);

			glass.Click += (_, _) => Show(true);
			end.Click += (_, _) => Show(false);
			box.TextChanged += (_, _) => Filter();
			box.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter && list?.FirstShown is { } first)
				{
					e.Handled = true;
					open(first);
				}
				else if (e.Key == Avalonia.Input.Key.Escape && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
				{
					e.Handled = true;
					Show(false);
				}
			};
		}

		/// <summary>The songs listed now (also after they're shown again, e.g. after moving them): the search applies to them.</summary>
		public void Use(SelectableList<SetlistEntry> songs, IReadOnlyList<SetlistEntry> shown)
		{
			list = songs;
			entries = shown;
			ShowFilters();
			Filter();
		}

		/// <summary>The setlist's tags (as in the songbook), then the media kinds its songs have, as buttons; those chosen stay chosen while the songs still have them.</summary>
		private void ShowFilters()
		{
			var tags = SongTags.AllOf(entries.Select(entry => entry.Song));
			chosenTags.IntersectWith(tags);
			withoutTags.IntersectWith(tags);
			var media = entries.SelectMany(entry => SongsView.MediaOf(entry.Song).Select(link => link.Site)).Distinct().OrderBy(site => site).ToList();
			chosenMedia.IntersectWith(media);
			withoutMedia.IntersectWith(media);

			filters.Children.Clear();
			foreach (var tag in tags)
			{
				var state = withoutTags.Contains(tag) ? TagFilterState.Without : chosenTags.Contains(tag) ? TagFilterState.With : TagFilterState.Off;
				filters.Children.Add(TagFilterButton.Create(tag, state, 12, new Avalonia.Thickness(8, 1), next =>
				{
					chosenTags.Remove(tag);
					withoutTags.Remove(tag);
					if (next == TagFilterState.With)
					{
						chosenTags.Add(tag);
					}
					else if (next == TagFilterState.Without)
					{
						withoutTags.Add(tag);
					}

					Filter();
				}));
			}

			// The media kinds after the tags.
			foreach (var site in media)
			{
				// As in the songbook.
				filters.Children.Add(SongsView.MediaFilterButton(site, SongsView.StateOf(site, chosenMedia, withoutMedia), next =>
				{
					SongsView.SetState(site, next, chosenMedia, withoutMedia);
					Filter();
				}));
			}

			filters.IsVisible = box.IsVisible && filters.Children.Count > 0;
		}

		/// <summary>Shows the search (true) or ends it, listing every song again (false).</summary>
		public void Show(bool searching)
		{
			if (box.IsVisible == searching)
			{
				return;
			}

			box.IsVisible = searching;
			glass.IsVisible = !searching;
			end.IsVisible = searching;
			filters.IsVisible = searching && filters.Children.Count > 0;
			SearchingChanged?.Invoke(searching);
			if (searching)
			{
				// Once it's on screen.
				Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
			}
			else
			{
				// Every song is listed again.
				box.Text = string.Empty;
				chosenTags.Clear();
				withoutTags.Clear();
				chosenMedia.Clear();
				withoutMedia.Clear();
				ShowFilters();
				Filter();
			}
		}

		private void Filter()
		{
			var text = box.Text?.Trim() ?? string.Empty;
			list?.Filter(entry =>
				(text.Length == 0 || HomeView.DrawerSongMatches(entry.Song, text))
				&& chosenTags.All(tag => SongTags.Has(entry.Song, tag))
				&& !withoutTags.Any(tag => SongTags.Has(entry.Song, tag))
				&& SongsView.HasMedia(entry.Song, chosenMedia, withoutMedia));
			Filtered?.Invoke();
		}
	}
}
