using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The band's setlists: one setlist with its songs (at first the current one), and the band's
	/// other setlists as buttons above it, flowing left to right; clicking one shows it instead. The
	/// current setlist is highlighted and has a check. The shown setlist's card has Make current
	/// (unless it is), add songs and delete; picking a song in it opens the song.
	/// </summary>
	public sealed class SetlistsView : UserControl
	{
		private const string AddIcon = "M12,5 V19 M5,12 H19";

		private readonly IReadOnlyList<SetlistSummary> setlists;
		private readonly Func<SetlistSummary, Task<IReadOnlyList<SetlistEntry>>> loadEntries;
		private readonly WrapPanel otherSetlists = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
		private readonly ContentControl details = new();
		private SetlistSummary? shownSetlist;

		/// <summary>Raised when the user picks a song of the shown setlist.</summary>
		public event Action<SetlistSummary, SetlistEntry>? SongSelected;

		/// <summary>Raised with the "+" button when the user wants to add songs to the shown setlist (to show the add menu at).</summary>
		public event Action<SetlistSummary, Button>? AddSongsRequested;

		/// <summary>Raised when the user makes a setlist the current one.</summary>
		public event Action<SetlistSummary>? SetlistMadeCurrent;

		/// <summary>Raised with the name when the user adds a setlist.</summary>
		public event Action<string>? SetlistCreateRequested;

		/// <summary>Raised with the new name when the user renames a setlist (clicking its name).</summary>
		public event Action<SetlistSummary, string>? SetlistRenameRequested;

		/// <summary>Raised when the user confirms deleting a setlist.</summary>
		public event Action<SetlistSummary>? SetlistDeleteRequested;

		/// <summary>The band whose setlists these are.</summary>
		public GroupSummary Group { get; }

		/// <summary>The setlist shown as the current one (when the page was made).</summary>
		public Guid? CurrentSetlistId { get; }

		/// <summary>The setlist whose songs are shown.</summary>
		public Guid? SelectedSetlistId => shownSetlist?.Id;

		/// <param name="currentSetlistId">The band's current setlist, shown highlighted.</param>
		/// <param name="loadEntries">Gets a setlist's songs, to show them.</param>
		/// <param name="selectedSetlistId">The setlist to show (e.g. one just added); else the current one.</param>
		public SetlistsView(
			GroupSummary group,
			IReadOnlyList<SetlistSummary> setlists,
			Guid? currentSetlistId,
			Func<SetlistSummary, Task<IReadOnlyList<SetlistEntry>>> loadEntries,
			Guid? selectedSetlistId = null)
		{
			Group = group;
			CurrentSetlistId = currentSetlistId;
			this.setlists = setlists;
			this.loadEntries = loadEntries;

			// "+" reveals a name box above the list; Add (or Enter) creates the setlist.
			var (addButton, nameRow) = PageLayout.AddByName(L.T("Add setlist"), L.T("Name of the new setlist"), name => SetlistCreateRequested?.Invoke(name));

			otherSetlists.IsVisible = setlists.Count > 1;
			var shown = setlists.FirstOrDefault(setlist => setlist.Id == selectedSetlistId)
				?? setlists.FirstOrDefault(setlist => setlist.Id == currentSetlistId)
				?? setlists.FirstOrDefault();
			ShowSetlist(shown);

			var column = new StackPanel { Spacing = 16, Children = { nameRow, otherSetlists, details } };
			if (setlists.Count == 0)
			{
				column.Children.Add(new TextBlock { Text = L.T("There are no setlists yet. Add one with +."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
			}

			Content = PageLayout.Page(
				L.T("Setlist"),
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				titleAction: addButton,
				closable: true,
				icon: AppIcons.Setlist);
		}

		/// <summary>
		/// Shows the setlist's card and songs (nothing when there's no setlist), and all the other
		/// setlists (this one highlighted) as buttons above it; clicking another shows that one instead.
		/// </summary>
		private void ShowSetlist(SetlistSummary? setlist)
		{
			shownSetlist = setlist;
			details.Content = setlist == null ? null : Details(setlist);

			otherSetlists.Children.Clear();
			foreach (var other in setlists)
			{
				// As wide as the name (and the current setlist's check); the one shown below is highlighted.
				var button = new Button { Content = PageLayout.RowContent(other.Name, other.Id == CurrentSetlistId), Padding = new Thickness(12, 6) };
				if (other.Id == setlist?.Id)
				{
					button.Classes.Add("accent");
				}
				else
				{
					button.Click += (_, _) => ShowSetlist(other);
				}

				otherSetlists.Children.Add(button);
			}
		}

		/// <summary>The setlist's name, whether it's the current one, its tools, and its songs (loaded here).</summary>
		private Control Details(SetlistSummary setlist)
		{
			var isCurrent = setlist.Id == CurrentSetlistId;
			var songCount = new TextBlock { Text = isCurrent ? L.T("Current setlist") : L.T("Not current"), Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			if (!isCurrent)
			{
				var makeCurrent = new Button { Content = L.T("Make current"), Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center, Height = 40, VerticalContentAlignment = VerticalAlignment.Center };
				makeCurrent.Click += (_, _) => SetlistMadeCurrent?.Invoke(setlist);
				actions.Children.Add(makeCurrent);
			}

			var addSongs = HomeView.IconButton(AddIcon, L.T("Add a song to the setlist"));
			addSongs.Click += (_, _) => AddSongsRequested?.Invoke(setlist, addSongs);
			actions.Children.Add(addSongs);
			actions.Children.Add(PageLayout.DeleteButton(
				L.T("Delete {0}", setlist.Name),
				L.T("Delete the setlist \"{0}\"? Its songs stay in the songbook.", setlist.Name),
				() => SetlistDeleteRequested?.Invoke(setlist)));

			DockPanel.SetDock(actions, Dock.Right);
			// The name (or the box to change it) on one line with the tools; the song count below.
			var header = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new DockPanel
					{
						Children =
						{
							actions,
							PageLayout.EditableTitle(setlist.Name, newName => SetlistRenameRequested?.Invoke(setlist, newName)),
						},
					},
					songCount,
				},
			};

			var songs = new ContentControl { Content = new TextBlock { Text = L.T("Loading songs…"), Opacity = 0.7 } };
			_ = LoadSongsAsync(setlist, songs, songCount, isCurrent);

			var card = new Border
			{
				Padding = new Thickness(16),
				CornerRadius = new CornerRadius(8),
				Child = new StackPanel { Spacing = 12, Children = { header, songs } },
			};
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			if (isCurrent)
			{
				// The current setlist's card is outlined in the accent colour, like its button.
				card.BorderThickness = new Thickness(2);
				card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("AccentButtonBackground"));
			}

			return card;
		}

		/// <summary>Fills in the setlist's songs, unless another setlist is shown by then.</summary>
		private async Task LoadSongsAsync(SetlistSummary setlist, ContentControl songs, TextBlock status, bool isCurrent)
		{
			IReadOnlyList<SetlistEntry> entries;
			try
			{
				entries = await loadEntries(setlist);
			}
			catch (Exception ex)
			{
				songs.Content = new TextBlock { Text = L.T("Couldn't get the songs. {0}", ex.Message), TextWrapping = TextWrapping.Wrap };
				return;
			}

			if (shownSetlist?.Id != setlist.Id)
			{
				return;
			}

			var count = entries.Count == 1 ? "1 song" : $"{entries.Count} songs";
			status.Text = isCurrent ? L.T("Current setlist · {0}", count) : count;
			if (entries.Count == 0)
			{
				songs.Content = new TextBlock { Text = L.T("There are no songs in this setlist yet. Add some with +."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
				return;
			}

			var rows = new StackPanel { Spacing = 4 };
			foreach (var entry in entries)
			{
				rows.Children.Add(PageLayout.ListRow(entry.Song.Name, PageLayout.SongInfo(entry.Song), () => SongSelected?.Invoke(setlist, entry)));
			}

			songs.Content = rows;
		}
	}
}
