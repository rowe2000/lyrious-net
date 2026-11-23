using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The songs of one setlist, in order; pick one to show its lyrics, or add songs from the songbook.
	/// The songs look as in the songbook (see <see cref="SongsView.SongRow"/>), with its check boxes
	/// (shared with it); a click selects a song and shows its toolbar (as in the songbook), a double
	/// click opens it, a longclick moves the songs (see <see cref="SetlistMoveList"/>).
	/// </summary>
	public sealed class SetlistView : UserControl
	{
		private const string AddIcon = "M12,5 V19 M5,12 H19";

		/// <summary>Raised when the user picks a song.</summary>
		public event Action<SetlistEntry>? SongSelected;


		/// <summary>Raised with the songs in their new order when the member moved one.</summary>
		public event Action<IReadOnlyList<SetlistEntry>>? SongsMoved;

		/// <summary>Raised when the user wants to add songs from the songbook.</summary>
		public event Action? AddSongsRequested;

		/// <param name="currentEntryId">The band's current song in this setlist (highlighted, with a check), if it's here.</param>
		/// <param name="songTools">What a selected song's toolbar does (as in the songbook); none, no toolbar.</param>
		public SetlistView(SetlistSummary setlist, IReadOnlyList<SetlistEntry> entries, Guid? currentEntryId = null, SongsView.SongToolActions? songTools = null)
		{
			var addButton = HomeView.IconButton(AddIcon, L.T("Add songs from the songbook"));
			addButton.Click += (_, _) => AddSongsRequested?.Invoke();

			// The header's tools: while songs are moved, only the ✓ that ends it.
			var titleTools = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Children = { addButton } };
			// "Uncheck all songs", while songs are checked: for the songs shown now (see List).
			IEnumerable<CheckBox> shownBoxes = [];
			Action dimShown = () => { };
			var uncheckAll = SongsView.UncheckAllButton(() => shownBoxes, () => dimShown());
			uncheckAll.IsVisible = false;
			titleTools.Children.Insert(0, uncheckAll);
			// Searching the songs, as in the right drawer.
			// The box takes the title's place while searching; the tools stay.
			var search = new SetlistSearch(entry => SongSelected?.Invoke(entry));
			titleTools.Children.Insert(0, search.Tool);
			search.SearchingChanged += searching =>
			{
				if (Content is Control page)
				{
					PageLayout.ShowInPlaceOfTitle(page, search.Box, searching);
				}
			};
			var body = new ContentControl();
			// Around the songs: a transparent border that flashes while they're moved (see SetlistMoveList.FlashBorder).
			// The tags and media kinds to narrow the search by, above the songs while searching.
			search.Filters.Margin = new Avalonia.Thickness(0, 0, 0, 8);
			DockPanel.SetDock(search.Filters, Dock.Top);
			var frame = new Border { Child = new DockPanel { Children = { search.Filters, body } }, BorderThickness = new Avalonia.Thickness(2), BorderBrush = Avalonia.Media.Brushes.Transparent, CornerRadius = new Avalonia.CornerRadius(8) };
			body.Content = List(entries);

			// The songs, as in the songbook; a longclick on one moves them, Done shows them so again.
			Control List(IReadOnlyList<SetlistEntry> shown)
			{
				var boxes = new List<CheckBox>();
				var ids = shown.Select(entry => entry.Id).ToArray();
				SelectableList<SetlistEntry>? list = null;
				// Checked songs hide the selected song's toolbar, as in the songbook.
				shownBoxes = boxes;
				dimShown = Dim;
				void Dim()
				{
					SongsView.DimCheckBoxes(boxes, SongsView.AnyEntryChecked(ids));
					// "47 songs · 2 checked" under the name while any are checked.
					if (Content is Control page && PageLayout.Subtitle(page) is { } subtitle)
					{
						subtitle.Text = SongsView.CountText(ids.Length, SongsView.CheckedEntries(ids), list?.ShownCount);
					}

					uncheckAll.IsVisible = SongsView.AnyEntryChecked(ids);
					if (list != null)
					{
						list.ToolsHidden = SongsView.AnyEntryChecked(ids);
					}
				}

				void StartMoving(SetlistEntry pressed)
				{
					// Every song is moved: the search ends (before the hint takes the count's place).
					search.Show(false);
					var moving = new SetlistMoveList(shown, pressed.Id, currentEntryId);
					moving.Moved += order =>
					{
						shown = order;
						SongsMoved?.Invoke(order);
					};

					var flash = SetlistMoveList.FlashBorder(frame);
					// How to move them, in place of "45 songs" under the name, until done.
					var subtitle = Content is Control page ? PageLayout.Subtitle(page) : null;
					var count = subtitle?.Text;
					if (subtitle != null)
					{
						subtitle.Text = moving.Hint;
					}

					Button? done = null;
					// Also when the page is left first.
					done = moving.DoneButton(() =>
					{
						flash.Dispose();
						if (subtitle != null)
						{
							subtitle.Text = count;
						}

						titleTools.Children.Remove(done!);
						addButton.IsVisible = true;
						search.Tool.IsVisible = true;
						if (body.Content == moving)
						{
							body.Content = List(shown);
						}
					});
					addButton.IsVisible = false;
					uncheckAll.IsVisible = false;
					search.Tool.IsVisible = false;
					titleTools.Children.Add(done);
					body.Content = moving;
				}

				list = new SelectableList<SetlistEntry>(
					shown,
					entry =>
					{
						var row = SongsView.CheckableRow(entry.Song, SongsView.SongRow(entry.Song, null, entry.Id == currentEntryId, SongsView.SongListExtraHeight), boxes, Dim, entry.Id);
						SetlistMoveList.OnLongClick(row, () => StartMoving(entry));
						return row;
					},
					entry => entry.Id == currentEntryId,
					entry => songTools == null ? [] : SongsView.ToolButtons(entry.Song, entry.Id == currentEntryId, () => SongSelected?.Invoke(entry), songTools),
					entry => SongSelected?.Invoke(entry),
					L.T("There are no songs in this setlist yet."),
					rowPadding: SongsView.SongListRowPadding,
					toolsBelow: true);
				// The count follows the search: "12 of 47 songs" while it leaves some out.
				search.Filtered = Dim;
				search.Use(list, shown);
				Dim();
				return list;
			}

			Content = PageLayout.Page(
				setlist.Name,
				frame,
				subtitle: SongsView.CountText(entries.Count, SongsView.CheckedEntries(entries.Select(entry => entry.Id))),
				titleAction: titleTools,
				closable: true);
		}
	}
}
