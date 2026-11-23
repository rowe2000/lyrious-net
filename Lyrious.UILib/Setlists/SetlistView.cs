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
				// The check tool just above the songs (none, some or all checked).
				// "47 songs · 2 checked" at the right of its line.
				var count = new TextBlock { FontSize = 13, Opacity = 0.7, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
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

					checkAll?.Refresh();
					if (list != null)
					{
						list.ToolsHidden = SongsView.AnyEntryChecked(ids);
					}
				}

				void StartMoving(SetlistEntry pressed, SetlistMoveList.HeldPointer? held)
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
					// How to move them, under the name, until done.
					var subtitle = Content is Control page ? PageLayout.Subtitle(page) : null;
					if (subtitle != null)
					{
						subtitle.Text = moving.Hint;
						subtitle.IsVisible = true;
					}

					Button? done = null;
					// Also when the page is left first.
					done = moving.DoneButton(() =>
					{
						flash.Dispose();
						if (subtitle != null)
						{
							subtitle.IsVisible = false;
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
					search.Tool.IsVisible = false;
					titleTools.Children.Add(done);
					body.Content = moving;
					// Still held (a finger or the mouse): the song goes on being dragged from there.
					if (held != null)
					{
						moving.ContinueDrag(held, pressed.Id);
					}
				}

				list = new SelectableList<SetlistEntry>(
					shown,
					entry =>
					{
						var row = SongsView.CheckableRow(entry.Song, SongsView.SongRow(entry.Song, null, entry.Id == currentEntryId, SongsView.SongListExtraHeight), boxes, Dim, entry.Id);
						SetlistMoveList.OnLongClick(row, held => StartMoving(entry, held));
						return row;
					},
					entry => entry.Id == currentEntryId,
					entry => songTools == null ? [] : SongsView.ToolButtons(entry.Song, entry.Id == currentEntryId, () => SongSelected?.Invoke(entry), songTools),
					entry => SongSelected?.Invoke(entry),
					L.T("There are no songs in this setlist yet."),
					rowPadding: SongsView.SongListRowPadding,
					spacing: SongsView.SongListSpacing,
					toolsBelow: true);
				// The count follows the search: "12 of 47 songs" while it leaves some out.
				search.Filtered = Dim;
				search.Use(list, shown);
				Dim();
				// The check tool, then the songs.
				return new DockPanel { Children = { checkRow, list } };
			}

			Content = PageLayout.Page(
				setlist.Name,
				frame,
				subtitle: " ",
				titleAction: titleTools,
				closable: true);
			// The line under the name only for how to move the songs (the count is beside "Check all").
			if (PageLayout.Subtitle((Control)Content) is { } line)
			{
				line.IsVisible = false;
			}
		}
	}
}
