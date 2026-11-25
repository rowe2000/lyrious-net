using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Sound files dropped (or picked) to be linked to songs, before anything is kept: each file with the
	/// song its name fits best (see <see cref="SoundMatching"/>), to change or leave unlinked, and Link and
	/// Cancel. Link keeps the chosen files on this device, adds each to its song's media links (after
	/// the song's other links) and sends it to the server for the band; files not linked aren't kept.
	/// Used by the Sound files page, and for files dropped on a songbook or a setlist.
	/// </summary>
	public sealed class SoundLinkProposals : UserControl
	{
		/// <summary>A song a file can be linked to, and how it's shown in the list (e.g. with its band).</summary>
		public sealed record Candidate(SongDetails Song, string Shown);

		/// <summary>The longest a song's media link field can be (as the lyrics page keeps it).</summary>
		private const int MaxMediaLinkLength = 500;

		private readonly ISoundStore sounds;
		private readonly ISetlistService service;

		/// <summary>Raised when the chosen files were linked: how many, and what went wrong with the others (empty when nothing did).</summary>
		public event Action<int, IReadOnlyList<string>>? Linked;

		/// <summary>Raised when Cancel (or the page's ✕) is pressed: nothing is kept.</summary>
		public event Action? Cancelled;

		/// <param name="files">The sound files, in the order they're listed.</param>
		/// <param name="candidates">The songs to link them to, as listed in each file's choice.</param>
		/// <param name="intro">What the files are matched against, said under the heading.</param>
		/// <param name="noMatch">Said under a file whose name fits no song.</param>
		/// <param name="pageTitle">Shown as a page of its own with this title (and a ✕); else only the list, inside another page.</param>
		public SoundLinkProposals(ISoundStore sounds, ISetlistService service, IReadOnlyList<IStorageFile> files, IReadOnlyList<Candidate> candidates,
			string intro, string noMatch, string? pageTitle = null)
		{
			this.sounds = sounds;
			this.service = service;
			var ordered = candidates.OrderBy(c => c.Shown, StringComparer.CurrentCultureIgnoreCase).ToList();
			var matches = SoundMatching.Match(files.Select(file => file.Name).ToList(), ordered.Select(c => c.Song).ToList());
			var dontLink = L.T("Don't link");
			var choices = new List<ComboBox>();
			var rows = new StackPanel { Spacing = 6 };
			for (var i = 0; i < files.Count; i++)
			{
				var items = new List<string> { dontLink };
				items.AddRange(ordered.Select(c => c.Shown));
				var match = matches[i];
				var choice = new ComboBox
				{
					ItemsSource = items,
					SelectedIndex = match == null ? 0 : ordered.FindIndex(c => c.Song.Id == match.Id) + 1,
					HorizontalAlignment = HorizontalAlignment.Stretch,
					MaxDropDownHeight = 320,
				};
				choices.Add(choice);
				var found = new TextBlock
				{
					Text = match == null ? noMatch : L.T("Fits {0}", match.Name),
					FontSize = 12,
					Opacity = 0.7,
				};
				var card = new Border
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
				};
				card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
				rows.Children.Add(card);
			}

			var link = PageLayout.OkButton(L.T("Link"));
			// An ✕ only (its tooltip says Cancel).
			var cancel = PageLayout.CancelButton(L.T("Cancel"));
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
			cancel.Click += (_, _) => Cancelled?.Invoke();
			link.Click += async (_, _) =>
			{
				link.IsEnabled = false;
				cancel.IsEnabled = false;
				var chosen = files.Select((file, i) => (file, index: choices[i].SelectedIndex - 1))
					.Where(c => c.index >= 0)
					.Select(c => (c.file, ordered[c.index].Song))
					.ToList();
				var problems = await LinkAsync(chosen);
				Linked?.Invoke(chosen.Count - problems.Count, problems);
			};

			var list = new DockPanel
			{
				Children =
				{
					new StackPanel
					{
						Spacing = 8,
						Margin = new Thickness(0, 0, 0, 8),
						Children =
						{
							new TextBlock { Text = L.T("Link the dropped sound files"), FontSize = 18, FontWeight = FontWeight.SemiBold, IsVisible = pageTitle == null },
							new TextBlock { Text = intro, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
							new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { link, cancel, count } },
						},
					}.Docked(Dock.Top),
					new ScrollViewer { Content = rows },
				},
			};

			if (pageTitle == null)
			{
				Content = list;
			}
			else
			{
				Content = PageLayout.Page(pageTitle, list, closable: true, icon: AppIcons.SoundFiles);
				// Its ✕ is a cancel too (not only going back): whoever showed it decides where to go.
				AddHandler(PageLayout.CloseRequestedEvent, (_, e) =>
				{
					e.Handled = true;
					Cancelled?.Invoke();
				});
			}
		}

		/// <summary>The sound files among what's dragged.</summary>
		public static IReadOnlyList<IStorageFile> DroppedSounds(DragEventArgs e)
			=> e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Where(file => MediaLinks.IsSoundFile(file.Name)).ToList() ?? [];

		/// <summary>
		/// Keeps each file on this device, adds it to its song's media links and sends it to the server for
		/// the band; what went wrong, one line per file it went wrong for.
		/// </summary>
		private async Task<IReadOnlyList<string>> LinkAsync(IReadOnlyList<(IStorageFile File, SongDetails Song)> chosen)
		{
			var problems = new List<string>();
			foreach (var (file, song) in chosen)
			{
				try
				{
					var sound = new SoundFile(MediaLinks.NewSoundId(file.Name), file.Name);
					// Room for it among the song's links checked first, so nothing is kept for nothing.
					if (MediaLinks.With(song.MediaLink, MediaLinks.SoundLink(sound, MaxMediaLinkLength), MaxMediaLinkLength, out var noRoom) is not { } field)
					{
						problems.Add(L.T("{0}: {1}", song.Name, L.T(noRoom!)));
						continue;
					}

					await using (var content = await file.OpenReadAsync())
					{
						if (content.CanSeek && content.Length > MediaLinks.MaxSoundSize)
						{
							problems.Add(L.T("{0} is too big: a sound file can be at most {1} MB.", file.Name, MediaLinks.MaxSoundSize / (1024 * 1024)));
							continue;
						}

						await sounds.SaveAsync(sound.Id, content);
					}

					var saved = await service.UpdateSongAsync(song with { MediaLink = field });
					SoundSync.Upload(saved.Id, sound.Id);
				}
				catch (Exception ex)
				{
					problems.Add(L.T("Couldn't keep {0}. {1}", file.Name, ex.Message));
				}
			}

			return problems;
		}
	}
}
