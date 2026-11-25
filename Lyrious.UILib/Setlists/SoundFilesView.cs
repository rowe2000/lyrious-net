using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The sound files kept on this device (see <see cref="ISoundStore"/>): each with its name and the
	/// songs that use it (from the bands' songs' media links), its size, whether it's still to be
	/// sent to the server, and a button to play it (how far it has played along the item's bottom); the
	/// total size at the top. Files checked can be exported (one as itself, more as a zip file) or
	/// deleted, from the tool in the page's toolbar. Deleting only frees this
	/// device: a song keeps its link, and the file is fetched from the server again when it's played.
	/// Files no song uses come first, and can be deleted all at once. Sound files dropped on the page
	/// are matched by name to songs without media, and listed to be linked to them (see <see cref="SoundLinkProposals"/>).
	/// </summary>
	public sealed class SoundFilesView : UserControl
	{
		private readonly ISoundStore sounds;
		private readonly ISetlistService service;
		private readonly ContentControl body = new();
		private readonly TextBlock summary = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
		private readonly TextBlock problem = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
		private readonly TextBlock notice = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, IsVisible = false };
		// The files checked to export, by id; kept while the list is made anew.
		private readonly HashSet<string> checkedIds = [];
		private List<Row> shownRows = [];
		// The items' check boxes, and the tool to check or uncheck them all (as in the songbook).
		private readonly List<CheckBox> checkBoxes = [];
		private readonly SongsView.CheckAllTool checkAll;
		private readonly Button exportButton = HomeView.IconButton(ExportIcon, L.T("Export or delete the checked files…"));

		// How far the items' check boxes are from their left edge: as far as from their top and bottom.
		private const double CheckBoxLeft = 7;

		// An arrow down onto a line: saving to the device.
		private const string ExportIcon = "M12,4 V15 M7,10 L12,15 L17,10 M5,20 H19";
		private readonly TextBlock dropHint = new() { Opacity = 0.7, FontSize = 13, TextWrapping = TextWrapping.Wrap, Text = L.T("Drop sound files here, or add them with +, to link them to songs that have no media yet.") };

		/// <param name="RecordingOf">The song it's the band's recording of (a play recorded, see PlayRecorder); null when it isn't one.</param>
		/// <param name="OnServer">Not on this device: only on the server (added on another device), fetched when it's played.</param>
		private sealed record Row(StoredSound Sound, string Name, IReadOnlyList<string> Songs, bool Pending, string? RecordingOf = null, bool OnServer = false)
		{
			/// <summary>Whether a song links to it, or it's a play's recording.</summary>
			public bool Used => Songs.Count > 0 || RecordingOf != null;
		}

		public SoundFilesView(ISoundStore sounds, ISetlistService service)
		{
			this.sounds = sounds;
			this.service = service;
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));

			var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4), Children = { summary, dropHint, problem, notice } };
			DockPanel.SetDock(top, Dock.Top);
			body.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			// "+": pick sound files, to link them to songs as dropped ones are.
			var addButton = PageLayout.AddButton(L.T("Add sound files"));
			addButton.Click += async (_, _) => await PickSoundsAsync();
			// The checked files' tool: a popup to export them, or delete them.
			exportButton.IsEnabled = false;
			exportButton.Click += (_, _) => ShowCheckedMenu();
			// Its check box in line with the items' (see CheckBoxLeft). Unchecking the boxes forgets them (see RowCard).
			checkAll = new SongsView.CheckAllTool(() => checkBoxes, ShowExportable, boxLeft: CheckBoxLeft, L.T("Check all files"), L.T("Uncheck all files"), uncheckAll: () => { });
			checkAll.Button.Margin = new Thickness(0, 0, 0, 2);
			var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { exportButton, addButton } };
			Content = PageLayout.Page(L.T("Sound files"), new DockPanel { Children = { top, body } }, subtitle: L.T("Kept on this device"), titleAction: tools, closable: true, icon: AppIcons.SoundFiles);

			// The marks follow when a file reaches the server.
			void OnPendingChanged() => Dispatcher.UIThread.Post(() => _ = LoadAsync());
			AttachedToVisualTree += (_, _) =>
			{
				SoundSync.PendingChanged += OnPendingChanged;
				_ = LoadAsync();
			};
			DetachedFromVisualTree += (_, _) => SoundSync.PendingChanged -= OnPendingChanged;

			DragDrop.SetAllowDrop(this, true);
			AddHandler(DragDrop.DragOverEvent, (_, e) =>
			{
				if (SoundLinkProposals.DroppedSounds(e).Count > 0)
				{
					e.DragEffects = DragDropEffects.Copy;
					e.Handled = true;
				}
			}, RoutingStrategies.Bubble, handledEventsToo: true);
			AddHandler(DragDrop.DropEvent, async (_, e) =>
			{
				var files = SoundLinkProposals.DroppedSounds(e);
				if (files.Count > 0)
				{
					e.Handled = true;
					await ProposeLinksAsync(files);
				}
			}, RoutingStrategies.Bubble, handledEventsToo: true);
		}

		/// <summary>Picks sound files from the device, then lists them to be linked to songs.</summary>
		private async Task PickSoundsAsync()
		{
			if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
			{
				ShowProblem(L.T("Files can't be picked here."));
				return;
			}

			var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = L.T("Add sound files"),
				AllowMultiple = true,
				FileTypeFilter =
				[
					new FilePickerFileType(L.T("Sound files")) { Patterns = MediaLinks.SoundExtensions.Select(extension => "*" + extension).ToArray() },
				],
			});
			var sounds = files.Where(file => MediaLinks.IsSoundFile(file.Name)).ToList();
			if (sounds.Count > 0)
			{
				await ProposeLinksAsync(sounds);
			}
		}

		/// <summary>
		/// The dropped files, each with the song without media its name fits (or another one to pick, or
		/// none); Link keeps the chosen files on this device and links them to their songs.
		/// </summary>
		private async Task ProposeLinksAsync(IReadOnlyList<IStorageFile> files)
		{
			problem.IsVisible = false;
			List<(SongDetails Song, string Shown)> candidates;
			try
			{
				var groups = await service.GetGroupsAsync();
				candidates = [];
				var seen = new HashSet<Guid>();
				foreach (var group in groups)
				{
					foreach (var song in await service.GetSongbookSongsAsync(group.Id))
					{
						if (MediaLinks.Split(song.MediaLink).Count == 0 && seen.Add(song.Id))
						{
							candidates.Add((song, groups.Count > 1 ? $"{song.Name} · {group.Name}" : song.Name));
						}
					}
				}
			}
			catch (Exception ex)
			{
				ShowProblem(L.T("Couldn't get the songs. {0}", ex.Message));
				return;
			}

			var proposals = new SoundLinkProposals(
				sounds,
				service,
				files,
				candidates.Select(c => new SoundLinkProposals.Candidate(c.Song, c.Shown)).ToList(),
				L.T("Each file is matched by its name to a song that has no media yet. Change the song if it's wrong; files that aren't linked aren't kept."),
				L.T("No song without media fits the name"));
			proposals.Cancelled += () => _ = LoadAsync();
			proposals.Linked += async (_, problems) =>
			{
				await LoadAsync();
				if (problems.Count > 0)
				{
					ShowProblem(string.Join("\n", problems));
				}
			};
			body.Content = proposals;
		}

		private async Task LoadAsync()
		{
			try
			{
				var stored = await sounds.ListAsync();
				var used = await SongsBySoundAsync();
				var recordings = await RecordingsBySoundAsync();
				var pending = SoundSync.PendingSoundIds;
				var rows = stored
					.Select(sound => used.TryGetValue(sound.Id, out var songs)
						? new Row(sound, songs.Name, songs.Songs, pending.Contains(sound.Id))
						: recordings.TryGetValue(sound.Id, out var recording)
							? new Row(sound, recording.Name, [], pending.Contains(sound.Id), recording.Song)
							: new Row(sound, sound.Id, [], pending.Contains(sound.Id)))
					.ToList();
				// The band's files that aren't on this device (added on another one): listed after them, to
				// play (fetched from the server and kept here then).
				var here = stored.Select(sound => sound.Id).ToHashSet();
				rows.AddRange(used.Where(pair => !here.Contains(pair.Key))
					.Select(pair => new Row(new StoredSound(pair.Key, 0), pair.Value.Name, pair.Value.Songs, false, OnServer: true)));
				rows.AddRange(recordings.Where(pair => !here.Contains(pair.Key) && !used.ContainsKey(pair.Key))
					.Select(pair => new Row(new StoredSound(pair.Key, 0), pair.Value.Name, [], false, pair.Value.Song, OnServer: true)));
				rows = rows
					.OrderBy(row => row.OnServer)
					.ThenBy(row => row.Used)
					.ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
					.ToList();
				Show(rows);
			}
			catch (Exception ex)
			{
				body.Content = null;
				ShowProblem(L.T("Couldn't list the sound files. {0}", ex.Message));
			}
		}

		/// <summary>The bands' recorded plays by their recording's sound file: its name, and the song played.</summary>
		private async Task<Dictionary<string, (string Name, string Song)>> RecordingsBySoundAsync()
		{
			var bySound = new Dictionary<string, (string Name, string Song)>();
			try
			{
				foreach (var group in await service.GetGroupsAsync())
				{
					foreach (var playlog in await service.GetPlaylogsAsync(group.Id))
					{
						foreach (var play in playlog.Plays)
						{
							if (MediaLinks.SoundOf(play.Recording) is { } sound)
							{
								bySound[sound.Id] = (sound.Name, play.SongName);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				// The recordings are still listed, by their ids.
				System.Diagnostics.Debug.WriteLine($"Couldn't find the recorded plays: {ex.Message}");
			}

			return bySound;
		}

		/// <summary>The bands' songs by the sound file their media link names: its name as added, and the songs.</summary>
		private async Task<Dictionary<string, (string Name, List<string> Songs)>> SongsBySoundAsync()
		{
			var bySound = new Dictionary<string, (string Name, List<string> Songs)>();
			try
			{
				foreach (var group in await service.GetGroupsAsync())
				{
					foreach (var song in await service.GetSongbookSongsAsync(group.Id))
					{
						foreach (var sound in MediaLinks.SoundsOf(song.MediaLink))
						{
							if (!bySound.TryGetValue(sound.Id, out var entry))
							{
								entry = (sound.Name, new List<string>());
								bySound[sound.Id] = entry;
							}

							if (!entry.Songs.Contains(song.Name))
							{
								entry.Songs.Add(song.Name);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				// The files are still listed, by their ids.
				ShowProblem(L.T("Couldn't find the songs that use them. {0}", ex.Message));
			}

			return bySound;
		}

		private void Show(List<Row> rows)
		{
			shownRows = rows;
			// A file that's gone (deleted) isn't checked any more.
			checkedIds.IntersectWith(rows.Select(row => row.Sound.Id));
			ShowExportable();

			var total = rows.Sum(row => row.Sound.Size);
			var kept = rows.Count(row => !row.OnServer);
			summary.Text = kept == 0
				? L.T("No sound files are kept on this device.")
				: L.Plural(kept, "{0} file, {1} in all. Deleting one only frees this device: a song that uses it fetches it from the server again when it's played.", "{0} files, {1} in all. Deleting one only frees this device: a song that uses it fetches it from the server again when it's played.", Size(total));

			// Up and down go through the files, the space bar checks one (see ListKeys).
			var list = ListKeys.Stepped(new StackPanel { Spacing = 4 });
			var unused = rows.Where(row => !row.Used && !row.Pending).ToList();
			if (unused.Count > 1)
			{
				list.Children.Add(DeleteUnusedButton(unused));
			}

			checkBoxes.Clear();
			var cards = rows.Select(RowCard).ToList();
			// (Out of the list shown before first: it's the same tool each time.)
			(checkAll.Button.Parent as Panel)?.Children.Remove(checkAll.Button);
			list.Children.Add(checkAll.Button);
			list.Children.AddRange(cards);
			ShowExportable();
			body.Content = rows.Count == 0 ? null : new ScrollViewer { Content = list };
		}

		private Control RowCard(Row row)
		{
			// A play's recording is named after its song: nothing more to say about it.
			var detail = row.RecordingOf != null
				? ""
				: row.Songs.Count == 0 ? L.T("Not used by any song") : L.T("Used by {0}", string.Join(", ", row.Songs));
			if (row.Pending)
			{
				detail = detail.Length > 0 ? detail + L.T(" · Not sent to the server yet") : L.T("Not sent to the server yet");
			}

			// One only on the server: a download button, which keeps it on this device (then listed as kept, to play).
			var player = new SoundItemPlayer(sounds, row.Sound.Id, L.T("Play {0}", row.Name), needsDownload: row.OnServer);
			player.Downloaded += () => _ = LoadAsync();
			DockPanel.SetDock(player.Button, Dock.Right);

			var size = new TextBlock { Text = row.OnServer ? L.T("On the server") : Size(row.Sound.Size), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
			ToolTip.SetTip(size, row.OnServer ? L.T("Not on this device yet: download it to play it") : null);
			DockPanel.SetDock(size, Dock.Right);

			// On one line, as the items in Sessions: the name, and the detail at the right of it (the whole
			// detail in its tooltip when it doesn't fit).
			var detailText = new TextBlock { Text = detail, Opacity = 0.7, MaxWidth = 240, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, IsVisible = detail.Length > 0 };
			ToolTip.SetTip(detailText, detail.Length > 0 ? detail : null);
			DockPanel.SetDock(detailText, Dock.Right);
			var text = new DockPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				Children =
				{
					detailText,
					new TextBlock { Text = row.Name, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
				},
			};

			// Checked, it's among the files exported.
			var check = new CheckBox { IsChecked = checkedIds.Contains(row.Sound.Id), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Margin = new Thickness(0, 0, 8, 0) };
			ToolTip.SetTip(check, L.T("Check to export it"));
			Avalonia.Automation.AutomationProperties.SetName(check, L.T("Export {0}", row.Name));
			// Only files on this device can be exported or deleted: one on the server keeps the room only.
			check.IsEnabled = !row.OnServer;
			check.Opacity = row.OnServer ? 0 : 1;
			if (!row.OnServer)
			{
				checkBoxes.Add(check);
			}
			check.IsCheckedChanged += (_, _) =>
			{
				if (check.IsChecked == true)
				{
					checkedIds.Add(row.Sound.Id);
				}
				else
				{
					checkedIds.Remove(row.Sound.Id);
				}

				ShowExportable();
			};
			DockPanel.SetDock(check, Dock.Left);

			// As high as the items in Sessions (the play button's height), the check box as far from the left
			// edge as from the top and bottom: its 20 px square in the 34 px row.
			var card = player.Card(new DockPanel { MinHeight = 34, Children = { check, player.Button, size, text } }, new Thickness(CheckBoxLeft, 0, 0, 0));
			card.CornerRadius = new CornerRadius(8);
			ListKeys.SetIsItem(card, true);
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		/// <summary>"Delete the N unused files", asking first.</summary>
		private Button DeleteUnusedButton(List<Row> unused)
		{
			var button = new Button { Content = L.T("Delete the {0} files no song uses ({1})", unused.Count, Size(unused.Sum(row => row.Sound.Size))), Margin = new Thickness(0, 0, 0, 4) };
			var confirm = PageLayout.DangerButton(L.T("Delete"));
			var cancel = new Button { Content = L.T("Cancel") };
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = L.T("Delete the {0} sound files no song uses from this device?", unused.Count), TextWrapping = TextWrapping.Wrap },
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } },
					},
				},
			};
			confirm.Click += (_, _) =>
			{
				flyout.Hide();
				_ = DeleteAsync(unused.Select(row => row.Sound.Id).ToList());
			};
			cancel.Click += (_, _) => flyout.Hide();
			button.Flyout = flyout;
			return button;
		}

		private async Task DeleteAsync(IReadOnlyList<string> ids)
		{
			problem.IsVisible = false;
			try
			{
				foreach (var id in ids)
				{
					await sounds.DeleteAsync(id);
				}
			}
			catch (Exception ex)
			{
				ShowProblem(L.T("Couldn't delete it. {0}", ex.Message));
			}

			await LoadAsync();
		}

		/// <summary>
		/// The checked files' tool: usable once a file is checked, saying how many it's for. The check
		/// boxes dim while none is checked, and the check-all tool shows how many are (as in the songbook).
		/// </summary>
		private void ShowExportable()
		{
			SongsView.DimCheckBoxes(checkBoxes, checkedIds.Count > 0);
			checkAll?.Refresh();
			var count = shownRows.Count(row => checkedIds.Contains(row.Sound.Id));
			exportButton.IsEnabled = count > 0;
			var tip = count == 0 ? L.T("Export or delete the checked files…") : L.Plural(count, "Export or delete the checked file…", "Export or delete the {0} checked files…");
			ToolTip.SetTip(exportButton, tip);
			Avalonia.Automation.AutomationProperties.SetName(exportButton, tip);
		}

		/// <summary>
		/// The popup of the checked files' tool: which files, then Export… and Delete…; Delete asks in the
		/// popup first (saying when a file hasn't reached the server, as it's lost then).
		/// </summary>
		private void ShowCheckedMenu()
		{
			var chosen = shownRows.Where(row => checkedIds.Contains(row.Sound.Id)).ToList();
			if (chosen.Count == 0)
			{
				return;
			}

			var popup = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
			var export = SongsView.PopupItem(L.T("Export…"), ExportIcon);
			export.Click += (_, _) =>
			{
				popup.Hide();
				_ = ExportAsync();
			};
			var delete = SongsView.PopupItem(L.T("Delete…"), AppIcons.Trash);

			// Delete asks first, in the popup.
			var lost = chosen.Count(row => row.Pending);
			var question = new TextBlock
			{
				Text = lost > 0
					? L.Plural(lost, "{0} of them hasn't reached the server yet: deleted, it's lost. Delete them from this device?", "{0} of them haven't reached the server yet: deleted, they're lost. Delete them from this device?")
					: L.T("Delete them from this device? A song that uses one fetches it from the server again when it's played."),
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = 280,
				Margin = new Thickness(10, 0),
			};
			var confirm = PageLayout.DangerButton(L.T("Delete"));
			var cancel = new Button { Content = L.T("Cancel") };
			var asking = new StackPanel
			{
				Spacing = 10,
				IsVisible = false,
				Children = { question, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 10, 4), Children = { confirm, cancel } } },
			};
			delete.Click += (_, _) =>
			{
				delete.IsVisible = export.IsVisible = false;
				asking.IsVisible = true;
			};
			cancel.Click += (_, _) => popup.Hide();
			confirm.Click += (_, _) =>
			{
				popup.Hide();
				_ = DeleteAsync(chosen.Select(row => row.Sound.Id).ToList());
			};

			popup.Content = new StackPanel
			{
				Spacing = 6,
				MinWidth = 220,
				Children =
				{
					new TextBlock { Text = L.Plural(chosen.Count, "The {0} checked file", "The {0} checked files"), Opacity = 0.8, Margin = new Thickness(10, 0, 10, 0) },
					export,
					delete,
					asking,
				},
			};
			popup.ShowAt(exportButton);
		}

		/// <summary>
		/// Saves the checked files where the user picks: one as itself (by its name), more together in a
		/// zip file. A file that isn't on this device any more is left out, and said so.
		/// </summary>
		private async Task ExportAsync()
		{
			var chosen = shownRows.Where(row => checkedIds.Contains(row.Sound.Id)).ToList();
			if (chosen.Count == 0)
			{
				return;
			}

			problem.IsVisible = notice.IsVisible = false;
			if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
			{
				ShowProblem(L.T("Saving files isn't possible here."));
				return;
			}

			var single = chosen.Count == 1;
			var stamp = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			var suggested = single ? FileName(chosen[0]) : $"lyrious-sounds-{stamp}.zip";
			var extension = Path.GetExtension(suggested);
			var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				Title = L.T("Export sound files"),
				SuggestedFileName = suggested,
				DefaultExtension = extension.TrimStart('.'),
				FileTypeChoices =
				[
					single
						? new FilePickerFileType(L.T("Sound file")) { Patterns = ["*" + extension] }
						: new FilePickerFileType(L.T("Zip file")) { Patterns = ["*.zip"] },
				],
			});
			if (file == null)
			{
				return;
			}

			exportButton.IsEnabled = false;
			notice.Text = L.T("Exporting…");
			notice.IsVisible = true;
			try
			{
				// Made here first, then written at once: the browser's files are only written to as a whole.
				using var content = new MemoryStream();
				var missing = new List<string>();
				if (single)
				{
					await using var sound = await sounds.OpenAsync(chosen[0].Sound.Id);
					if (sound == null)
					{
						missing.Add(chosen[0].Name);
					}
					else
					{
						await sound.CopyToAsync(content);
					}
				}
				else
				{
					using (var zip = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
					{
						var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						foreach (var row in chosen)
						{
							await using var sound = await sounds.OpenAsync(row.Sound.Id);
							if (sound == null)
							{
								missing.Add(row.Name);
								continue;
							}

							// Sound files are compressed already: stored as they are.
							var entry = zip.CreateEntry(UniqueName(FileName(row), names), CompressionLevel.NoCompression);
							await using var into = entry.Open();
							await sound.CopyToAsync(into);
						}
					}
				}

				if (missing.Count < chosen.Count)
				{
					await using var output = await file.OpenWriteAsync();
					content.Position = 0;
					await content.CopyToAsync(output);
				}

				notice.Text = missing.Count == 0
					? L.Plural(chosen.Count, "Exported {0} file to {1}.", "Exported {0} files to {1}.", file.Name)
					: L.T("Exported to {0}. Not on this device any more, so left out: {1}", file.Name, string.Join(", ", missing));
			}
			catch (Exception ex)
			{
				notice.IsVisible = false;
				ShowProblem(L.T("Couldn't export. {0}", ex.Message));
			}
			finally
			{
				ShowExportable();
			}
		}

		/// <summary>The file's name to save it under: its name, with the file's extension, without what a file name can't have.</summary>
		private static string FileName(Row row)
		{
			var name = string.IsNullOrWhiteSpace(row.Name) ? row.Sound.Id : row.Name.Trim();
			var extension = Path.GetExtension(row.Sound.Id);
			if (Path.GetExtension(name).Length == 0 && extension.Length > 0)
			{
				name += extension;
			}

			foreach (var invalid in Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']))
			{
				name = name.Replace(invalid, '_');
			}

			return name;
		}

		/// <summary>The name, made different from those taken already in the zip file ("Song (2).ogg"), and taken.</summary>
		private static string UniqueName(string name, HashSet<string> taken)
		{
			var unique = name;
			for (var i = 2; !taken.Add(unique); i++)
			{
				unique = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
			}

			return unique;
		}

		private void ShowProblem(string message)
		{
			problem.Text = message;
			problem.IsVisible = true;
		}

		/// <summary>"3.2 MB", "640 KB".</summary>
		private static string Size(long bytes) => bytes >= 1024 * 1024
			? $"{(bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.CurrentCulture)} MB"
			: $"{Math.Max(1, (bytes + 1023) / 1024)} KB";
	}
}

namespace Lyrious.UILib.Setlists
{
	internal static class DockExtensions
	{
		/// <summary>The control, docked to that side in a <see cref="DockPanel"/>.</summary>
		public static T Docked<T>(this T control, Dock dock) where T : Control
		{
			DockPanel.SetDock(control, dock);
			return control;
		}
	}
}
