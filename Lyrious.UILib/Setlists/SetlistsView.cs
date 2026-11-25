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
		private readonly Guid? currentEntryId;
		private readonly WrapPanel otherSetlists = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
		private readonly ContentControl details = new();
		private SetlistSummary? shownSetlist;

		/// <summary>Raised when the user picks a song of the shown setlist.</summary>
		public event Action<SetlistSummary, SetlistEntry>? SongSelected;

		/// <summary>Raised with a setlist's songs in their new order when the member moved one (see <see cref="SetlistMoveList"/>).</summary>
		public event Action<SetlistSummary, IReadOnlyList<SetlistEntry>>? SongsMoved;

		// A play triangle, for Play (making the setlist the current one).
		private const string PlayIcon = "M7,4.5 L19,12 L7,19.5 Z";

		// What a selected song's toolbar does (as in the songbook); none, no toolbar.
		private readonly SongsView.SongToolActions? songTools;

		/// <summary>Raised with the "+" button when the user wants to add songs to the shown setlist (to show the add menu at).</summary>
		public event Action<SetlistSummary, Button>? AddSongsRequested;

		/// <summary>Raised when the user makes a setlist the current one.</summary>
		/// <summary>Raised when a Spotify song's link is dropped on a song's row: the song and the link.</summary>
		public event Action<SongDetails, string>? SpotifyLinkDropped;

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
		/// <param name="currentEntryId">The band's current song in the current setlist (highlighted, with a check).</param>
		/// <param name="songTools">What a selected song's toolbar does (as in the songbook).</param>
		public SetlistsView(
			GroupSummary group,
			IReadOnlyList<SetlistSummary> setlists,
			Guid? currentSetlistId,
			Func<SetlistSummary, Task<IReadOnlyList<SetlistEntry>>> loadEntries,
			Guid? selectedSetlistId = null,
			Guid? currentEntryId = null,
			SongsView.SongToolActions? songTools = null)
		{
			this.songTools = songTools;
			Group = group;
			CurrentSetlistId = currentSetlistId;
			this.currentEntryId = currentEntryId;
			this.setlists = setlists;
			this.loadEntries = loadEntries;

			// "+" reveals a name box above the list; Add (or Enter) creates the setlist.
			var (addButton, nameRow) = PageLayout.AddByName(L.T("Add setlist"), L.T("Name of the new setlist"), name => SetlistCreateRequested?.Invoke(name));

			otherSetlists.IsVisible = setlists.Count > 1;
			// A double-click on a setlist plays it.
			otherSetlists.AddHandler(PointerPressedEvent, OnSetlistsPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
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
				var button = new Button { Content = PageLayout.RowContent(other.Name, other.Id == CurrentSetlistId), Padding = new Thickness(12, 6), Tag = other };
				ToolTip.SetTip(button, L.T("Click to show it, double-click to play it"));
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

		/// <summary>
		/// A double-click on a setlist's button (see the constructor): plays it, as its Play does (the band's
		/// current setlist), unless it's the current one already.
		/// </summary>
		private void OnSetlistsPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
		{
			// The second press of a double-click (the first showed the setlist, making the buttons anew).
			if (e.ClickCount == 2
			    && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(e.Source as Avalonia.Visual, includeSelf: true) is { Tag: SetlistSummary setlist }
			    && setlist.Id != CurrentSetlistId)
			{
				e.Handled = true;
				SetlistMadeCurrent?.Invoke(setlist);
			}
		}

		/// <summary>The setlist's name, whether it's the current one, its tools, and its songs (loaded here).</summary>
		private Control Details(SetlistSummary setlist)
		{
			var isCurrent = setlist.Id == CurrentSetlistId;
			// How to move the songs, while they're moved (the song count is beside "Check all", see LoadSongsAsync).
			var songCount = new TextBlock { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, IsVisible = false };

			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			if (!isCurrent)
			{
				// "▶ Play": makes it the band's current setlist, to play from. The triangle in the button's text colour.
				var playIcon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(PlayIcon), Width = 16, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
				playIcon.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, playIcon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
				var makeCurrent = new Button
				{
					Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { playIcon, new TextBlock { Text = L.T("Play"), VerticalAlignment = VerticalAlignment.Center } } },
					Classes = { "accent" },
					VerticalAlignment = VerticalAlignment.Center,
					Height = 40,
					VerticalContentAlignment = VerticalAlignment.Center,
				};
				ToolTip.SetTip(makeCurrent, L.T("Make this the band's current setlist, to play"));
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
			// Searching its songs, as in the right drawer: the box takes the name's place, the tools stay.
			// The tools in order: Play (when not current), search, uncheck all (while songs are checked,
			// see LoadSongsAsync), add songs, delete.
			var search = new SetlistSearch(entry => SongSelected?.Invoke(setlist, entry));
			actions.Children.Insert(isCurrent ? 0 : 1, search.Tool);
			var title = PageLayout.EditableTitle(setlist.Name, newName => SetlistRenameRequested?.Invoke(setlist, newName));
			search.SearchingChanged += searching => title.IsVisible = !searching;

			// The name (or the box to change it, or the search) on one line with the tools; the search's filters and the song count below.
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
							new Panel { Children = { title, search.Box } },
						},
					},
					// The tags and media kinds to narrow the search by, while searching: right under the
					// search box, above "Current setlist · 47 songs".
					search.Filters,
					songCount,
				},
			};
			search.Filters.Margin = new Thickness(0, 6, 0, 4);

			var songs = new ContentControl { Content = new TextBlock { Text = L.T("Loading songs…"), Opacity = 0.7 } };
			Border? card = null;
			_ = LoadSongsAsync(setlist, songs, songCount, isCurrent, actions, () => card, search);

			// Outlined by a transparent border when not current, so moving songs can flash it (see SetlistMoveList.FlashBorder).
			card = new Border
			{
				Padding = new Thickness(16),
				CornerRadius = new CornerRadius(8),
				BorderThickness = new Thickness(2),
				BorderBrush = Brushes.Transparent,
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
		/// <param name="actions">The setlist's tools: while songs are moved they're hidden, with the ✓ that ends it in their place.</param>
		/// <param name="card">The setlist's card, whose border flashes while songs are moved.</param>
		/// <param name="search">The search of the setlist's songs, which applies to them as they're listed.</param>
		private async Task LoadSongsAsync(SetlistSummary setlist, ContentControl songs, TextBlock status, bool isCurrent, Panel actions, Func<Border?> card, SetlistSearch search)
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

			status.IsVisible = false;
			if (entries.Count == 0)
			{
				songs.Content = new TextBlock { Text = L.T("There are no songs in this setlist yet. Add some with +."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
				return;
			}

			// The songs as in the songbook, with its check boxes (shared with it); a click selects one and
			// shows its toolbar under it, a double click opens it, a longclick moves them (see
			// SetlistMoveList). The band's current song is highlighted in the current setlist.
			var current = isCurrent ? currentEntryId : null;
			void StartMoving(SetlistEntry pressed, SetlistMoveList.HeldPointer? held)
			{
				// Every song is moved: the search ends.
				search.Show(false);
				var moving = new SetlistMoveList(entries, pressed.Id, current);
				moving.Moved += order =>
				{
					entries = order;
					SongsMoved?.Invoke(setlist, order);
				};

				var tools = actions.Children.ToList();
				var flash = card() is { } border ? SetlistMoveList.FlashBorder(border) : null;
				Button? done = null;
				// Also when the page (or another setlist) takes the list's place first.
				done = moving.DoneButton(() =>
				{
					flash?.Dispose();
					actions.Children.Remove(done!);
					tools.ForEach(tool => tool.IsVisible = true);
					if (songs.Content == moving)
					{
						_ = LoadSongsAsync(setlist, songs, status, isCurrent, actions, card, search);
					}
				});
				tools.ForEach(tool => tool.IsVisible = false);
				actions.Children.Add(done);
				// How to move them, in place of "Current setlist · 45 songs" (put back when the songs show again).
				status.Text = moving.Hint;
				status.IsVisible = true;
				songs.Content = moving;
				// Still held (a finger or the mouse): the song goes on being dragged from there.
				if (held != null)
				{
					moving.ContinueDrag(held, pressed.Id);
				}
			}

			var boxes = new List<CheckBox>();
			var ids = entries.Select(entry => entry.Id).ToArray();
			SelectableList<SetlistEntry>? list = null;

			// The check tool just above the songs, with "47 songs · 2 checked" at the right of its line
			// (made anew with the songs; moving songs hides them with them).
			var count = new TextBlock { FontSize = 13, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
			SongsView.CheckAllTool? checkAll = null;
			checkAll = new SongsView.CheckAllTool(() => boxes, Dim);
			DockPanel.SetDock(count, Dock.Right);
			var checkRow = new DockPanel { Children = { count, checkAll.Button } };
			DockPanel.SetDock(checkRow, Dock.Top);

			// Checked songs hide the selected song's toolbar, as in the songbook.
			void Dim()
			{
				SongsView.DimCheckBoxes(boxes, SongsView.AnyEntryChecked(ids));
				count.Text = SongsView.CountText(ids.Length, SongsView.CheckedEntries(ids), list?.ShownCount);
				// None, some or all checked.
				checkAll?.Refresh();

				if (list != null)
				{
					list.ToolsHidden = SongsView.AnyEntryChecked(ids);
				}
			}

			list = new SelectableList<SetlistEntry>(
				entries,
				entry =>
				{
					var row = SongsView.CheckableRow(entry.Song, SongsView.SongRow(entry.Song, null, entry.Id == current, SongsView.SongListExtraHeight), boxes, Dim, entry.Id);
					SetlistMoveList.OnLongClick(row, held => StartMoving(entry, held));
					return row;
				},
				entry => entry.Id == current,
				entry => songTools == null ? [] : SongsView.ToolButtons(entry.Song, entry.Id == current, () => SongSelected?.Invoke(setlist, entry), songTools),
				entry => SongSelected?.Invoke(setlist, entry),
				L.T("There are no songs in this setlist yet. Add some with +."),
				rowPadding: SongsView.SongListRowPadding,
				spacing: SongsView.SongListSpacing,
				toolsBelow: true);
			// The check tool, then the songs.
			songs.Content = new DockPanel { Children = { checkRow, list } };
			// The count follows the search: "12 of 47 songs" while it leaves some out.
			list.SpotifyLinkDropped += (entry, link) => SpotifyLinkDropped?.Invoke(entry.Song, link);
			search.Filtered = Dim;
			search.Use(list, entries);
			Dim();
		}
	}
}
