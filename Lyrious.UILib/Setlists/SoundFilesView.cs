using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
	/// sent to the server, and a delete button; the total size at the top. Deleting only frees this
	/// device: a song keeps its link, and the file is fetched from the server again when it's played.
	/// Files no song uses come first, and can be deleted all at once. Sound files dropped on the page
	/// are matched by name to songs without media, and listed to be linked to them (see <see cref="SoundMatching"/>).
	/// </summary>
	public sealed class SoundFilesView : UserControl
	{
		private readonly ISoundStore sounds;
		private readonly ISetlistService service;
		private readonly ContentControl body = new();
		private readonly TextBlock summary = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
		private readonly TextBlock problem = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
		private readonly TextBlock dropHint = new() { Opacity = 0.7, FontSize = 13, TextWrapping = TextWrapping.Wrap, Text = L.T("Drop sound files here, or add them with +, to link them to songs that have no media yet.") };
		private const int MaxMediaLinkLength = 500;

		/// <param name="RecordingOf">The song it's the band's recording of (a play recorded, see PlayRecorder); null when it isn't one.</param>
		private sealed record Row(StoredSound Sound, string Name, IReadOnlyList<string> Songs, bool Pending, string? RecordingOf = null)
		{
			/// <summary>Whether a song links to it, or it's a play's recording.</summary>
			public bool Used => Songs.Count > 0 || RecordingOf != null;
		}

		public SoundFilesView(ISoundStore sounds, ISetlistService service)
		{
			this.sounds = sounds;
			this.service = service;
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));

			var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4), Children = { summary, dropHint, problem } };
			DockPanel.SetDock(top, Dock.Top);
			body.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			// "+": pick sound files, to link them to songs as dropped ones are.
			var addButton = PageLayout.AddButton(L.T("Add sound files"));
			addButton.Click += async (_, _) => await PickSoundsAsync();
			Content = PageLayout.Page(L.T("Sound files"), new DockPanel { Children = { top, body } }, subtitle: L.T("Kept on this device"), titleAction: addButton, closable: true, icon: AppIcons.SoundFiles);

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
				if (DroppedSounds(e).Count > 0)
				{
					e.DragEffects = DragDropEffects.Copy;
					e.Handled = true;
				}
			}, RoutingStrategies.Bubble, handledEventsToo: true);
			AddHandler(DragDrop.DropEvent, async (_, e) =>
			{
				var files = DroppedSounds(e);
				if (files.Count > 0)
				{
					e.Handled = true;
					await ProposeLinksAsync(files);
				}
			}, RoutingStrategies.Bubble, handledEventsToo: true);
		}

		/// <summary>The border with a card's background.</summary>
		private static Border Card(Border border)
		{
			border.Bind(Border.BackgroundProperty, border.GetResourceObservable(LyriousTheme.CardBrush));
			return border;
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

		/// <summary>The sound files among what's dragged.</summary>
		private static IReadOnlyList<IStorageFile> DroppedSounds(DragEventArgs e)
			=> e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Where(file => MediaLinks.IsSoundFile(file.Name)).ToList() ?? [];

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

			candidates = candidates.OrderBy(c => c.Shown, StringComparer.CurrentCultureIgnoreCase).ToList();
			var matches = SoundMatching.Match(files.Select(file => file.Name).ToList(), candidates.Select(c => c.Song).ToList());
			var noSong = L.T("Don't link");
			var choices = new List<ComboBox>();
			var rows = new StackPanel { Spacing = 6 };
			for (var i = 0; i < files.Count; i++)
			{
				var items = new List<string> { noSong };
				items.AddRange(candidates.Select(c => c.Shown));
				var match = matches[i];
				var choice = new ComboBox
				{
					ItemsSource = items,
					SelectedIndex = match == null ? 0 : candidates.FindIndex(c => c.Song.Id == match.Id) + 1,
					HorizontalAlignment = HorizontalAlignment.Stretch,
					MaxDropDownHeight = 320,
				};
				choices.Add(choice);
				var found = new TextBlock
				{
					Text = match == null ? L.T("No song without media fits the name") : L.T("Fits {0}", match.Name),
					FontSize = 12,
					Opacity = 0.7,
				};
				rows.Children.Add(Card(new Border
				{
					Padding = new Thickness(12, 8),
					CornerRadius = new CornerRadius(8),
					Child = new StackPanel
					{
						Spacing = 4,
						Children =
						{
							new TextBlock { Text = files[i].Name, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis },
							found,
							choice,
						},
					},
				}));
			}

			var link = PageLayout.OkButton(L.T("Link"));
			var cancel = new Button { Content = L.T("Cancel") };
			var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };

			void ShowCount()
			{
				var n = choices.Count(c => c.SelectedIndex > 0);
				count.Text = L.Plural(n, "{0} file to link", "{0} files to link");
				link.IsEnabled = n > 0;
			}

			foreach (var choice in choices)
			{
				choice.SelectionChanged += (_, _) => ShowCount();
			}

			ShowCount();
			cancel.Click += (_, _) => _ = LoadAsync();
			link.Click += async (_, _) =>
			{
				link.IsEnabled = false;
				cancel.IsEnabled = false;
				var chosen = files.Select((file, i) => (file, index: choices[i].SelectedIndex - 1))
					.Where(c => c.index >= 0)
					.Select(c => (c.file, candidates[c.index].Song))
					.ToList();
				await LinkAsync(chosen);
			};

			body.Content = new DockPanel
			{
				Children =
				{
					new StackPanel
					{
						Spacing = 8,
						Margin = new Thickness(0, 0, 0, 8),
						Children =
						{
							new TextBlock { Text = L.T("Link the dropped sound files"), FontSize = 18, FontWeight = FontWeight.SemiBold },
							new TextBlock { Text = L.T("Each file is matched by its name to a song that has no media yet. Change the song if it's wrong; files that aren't linked aren't kept."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
							new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { link, cancel, count } },
						},
					}.Docked(Dock.Top),
					new ScrollViewer { Content = rows },
				},
			};
		}

		/// <summary>Keeps each file on this device, links it to its song, and sends it to the server for the band.</summary>
		private async Task LinkAsync(IReadOnlyList<(IStorageFile File, SongDetails Song)> chosen)
		{
			var problems = new List<string>();
			foreach (var (file, song) in chosen)
			{
				try
				{
					var sound = new SoundFile(MediaLinks.NewSoundId(file.Name), file.Name);
					await using (var content = await file.OpenReadAsync())
					{
						if (content.CanSeek && content.Length > MediaLinks.MaxSoundSize)
						{
							problems.Add(L.T("{0} is too big: a sound file can be at most {1} MB.", file.Name, MediaLinks.MaxSoundSize / (1024 * 1024)));
							continue;
						}

						await sounds.SaveAsync(sound.Id, content);
					}

					var saved = await service.UpdateSongAsync(song with { MediaLink = MediaLinks.SoundLink(sound, MaxMediaLinkLength) });
					SoundSync.Upload(saved.Id, sound.Id);
				}
				catch (Exception ex)
				{
					problems.Add(L.T("Couldn't keep {0}. {1}", file.Name, ex.Message));
				}
			}

			await LoadAsync();
			if (problems.Count > 0)
			{
				ShowProblem(string.Join("\n", problems));
			}
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
					.OrderBy(row => row.Used)
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
			var total = rows.Sum(row => row.Sound.Size);
			summary.Text = rows.Count == 0
				? L.T("No sound files are kept on this device.")
				: L.Plural(rows.Count, "{0} file, {1} in all. Deleting one only frees this device: a song that uses it fetches it from the server again when it's played.", "{0} files, {1} in all. Deleting one only frees this device: a song that uses it fetches it from the server again when it's played.", Size(total));

			var list = new StackPanel { Spacing = 4 };
			var unused = rows.Where(row => !row.Used && !row.Pending).ToList();
			if (unused.Count > 1)
			{
				list.Children.Add(DeleteUnusedButton(unused));
			}

			list.Children.AddRange(rows.Select(RowCard));
			body.Content = rows.Count == 0 ? null : new ScrollViewer { Content = list };
		}

		private Control RowCard(Row row)
		{
			var detail = row.RecordingOf != null
				? L.T("The band's recording of {0}", row.RecordingOf)
				: row.Songs.Count == 0 ? L.T("Not used by any song") : L.T("Used by {0}", string.Join(", ", row.Songs));
			if (row.Pending)
			{
				detail += L.T(" · Not sent to the server yet");
			}

			var question = row.Pending
				? L.T("{0} hasn't reached the server yet: deleted, it's lost. Delete it?", row.Name)
				: !row.Used
					? L.T("Delete {0}? No song uses it.", row.Name)
					: L.T("Delete {0} from this device? It's fetched from the server again when it's played.", row.Name);
			var delete = PageLayout.DeleteButton(L.T("Delete {0}", row.Name), question, () => _ = DeleteAsync([row.Sound.Id]));
			delete.VerticalAlignment = VerticalAlignment.Center;
			DockPanel.SetDock(delete, Dock.Right);

			var size = new TextBlock { Text = Size(row.Sound.Size), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
			DockPanel.SetDock(size, Dock.Right);

			var text = new StackPanel
			{
				Spacing = 2,
				VerticalAlignment = VerticalAlignment.Center,
				Children =
				{
					new TextBlock { Text = row.Name, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis },
					new TextBlock { Text = detail, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
				},
			};

			var card = new Border
			{
				Padding = new Thickness(12, 8, 4, 8),
				CornerRadius = new CornerRadius(8),
				Child = new DockPanel { Children = { delete, size, text } },
			};
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
