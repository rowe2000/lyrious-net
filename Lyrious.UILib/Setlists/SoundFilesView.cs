using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
	/// Files no song uses come first, and can be deleted all at once.
	/// </summary>
	public sealed class SoundFilesView : UserControl
	{
		private readonly ISoundStore sounds;
		private readonly ISetlistService service;
		private readonly ContentControl body = new();
		private readonly TextBlock summary = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
		private readonly TextBlock problem = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };

		private sealed record Row(StoredSound Sound, string Name, IReadOnlyList<string> Songs, bool Pending);

		public SoundFilesView(ISoundStore sounds, ISetlistService service)
		{
			this.sounds = sounds;
			this.service = service;
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));

			var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4), Children = { summary, problem } };
			DockPanel.SetDock(top, Dock.Top);
			body.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			Content = PageLayout.Page(L.T("Sound files"), new DockPanel { Children = { top, body } }, subtitle: L.T("Kept on this device"), closable: true, icon: AppIcons.SoundFiles);

			// The marks follow when a file reaches the server.
			void OnPendingChanged() => Dispatcher.UIThread.Post(() => _ = LoadAsync());
			AttachedToVisualTree += (_, _) =>
			{
				SoundSync.PendingChanged += OnPendingChanged;
				_ = LoadAsync();
			};
			DetachedFromVisualTree += (_, _) => SoundSync.PendingChanged -= OnPendingChanged;
		}

		private async Task LoadAsync()
		{
			try
			{
				var stored = await sounds.ListAsync();
				var used = await SongsBySoundAsync();
				var pending = SoundSync.PendingSoundIds;
				var rows = stored
					.Select(sound => used.TryGetValue(sound.Id, out var songs)
						? new Row(sound, songs.Name, songs.Songs, pending.Contains(sound.Id))
						: new Row(sound, sound.Id, [], pending.Contains(sound.Id)))
					.OrderBy(row => row.Songs.Count > 0)
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
			var unused = rows.Where(row => row.Songs.Count == 0 && !row.Pending).ToList();
			if (unused.Count > 1)
			{
				list.Children.Add(DeleteUnusedButton(unused));
			}

			list.Children.AddRange(rows.Select(RowCard));
			body.Content = rows.Count == 0 ? null : new ScrollViewer { Content = list };
		}

		private Control RowCard(Row row)
		{
			var detail = row.Songs.Count == 0 ? L.T("Not used by any song") : L.T("Used by {0}", string.Join(", ", row.Songs));
			if (row.Pending)
			{
				detail += L.T(" · Not sent to the server yet");
			}

			var question = row.Pending
				? L.T("{0} hasn't reached the server yet: deleted, it's lost. Delete it?", row.Name)
				: row.Songs.Count == 0
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
