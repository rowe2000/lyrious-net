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
	/// About the app: its name, version, branch and commit and what it's for, and the release notes
	/// kept on the server (see <see cref="ISetlistService.GetReleaseNotesAsync"/>) for the branch the
	/// app was built from: this release's first, then the previous ones, newest first. Others see only
	/// published notes ("No release notes yet…" when there are none for this release); a site manager
	/// also sees unpublished ones (drafts), edits each note's text and whether it's published, and adds
	/// this release's note when there's none yet. Without the server, the notes built into the app
	/// (<see cref="ReleaseNotes.All"/>).
	/// </summary>
	public sealed class AboutView : UserControl
	{
		private readonly StackPanel notes = new() { Spacing = 12 };
		private readonly ISetlistService? service;
		private readonly bool canEdit;

		/// <param name="service">Where the release notes are read (and, by a site manager, saved); without it, the notes built into the app.</param>
		public AboutView(ISetlistService? service = null)
		{
			this.service = service;
			canEdit = service != null && App.AuthService.IsSiteManager;
			var column = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					new TextBlock { Text = L.T("Lyrious"), FontSize = 24, FontWeight = FontWeight.SemiBold },
					new TextBlock { Text = L.T("Version {0}", ReleaseNotes.AppVersion), Opacity = 0.7 },
					new TextBlock
					{
						Text = L.T("Branch {0}", ReleaseNotes.AppBranch ?? ""),
						Opacity = 0.7,
						IsVisible = ReleaseNotes.AppBranch is not null,
					},
					new TextBlock
					{
						Text = L.T("Commit {0}", ReleaseNotes.AppCommit ?? ""),
						Opacity = 0.7,
						IsVisible = ReleaseNotes.AppCommit is not null,
					},
					new TextBlock
					{
						Text = L.T("Setlists, songbooks and lyrics for bands: lyrics and chords that scroll in time with the song, shared and followed live by the whole band."),
						TextWrapping = TextWrapping.Wrap,
					},
					notes,
				},
			};

			Content = PageLayout.Page(
				L.T("About"),
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				closable: true,
				icon: AppIcons.About);

			if (service != null)
			{
				notes.Children.Add(new TextBlock { Text = L.T("Reading the release notes…"), Opacity = 0.7 });
				_ = ShowServerNotesAsync();
			}
			else
			{
				ShowBuiltInNotes();
			}
		}

		/// <summary>
		/// The server's release notes for this build's branch (all of them when the build didn't record
		/// its branch): this release's (its version, else the newest), then the previous ones.
		/// </summary>
		private async Task ShowServerNotesAsync()
		{
			IReadOnlyList<ReleaseNoteEntry> all;
			try
			{
				all = await service!.GetReleaseNotesAsync();
			}
			catch (Exception)
			{
				// Offline (or an older server): the notes the app was built with.
				ShowBuiltInNotes();
				return;
			}

			var branch = ReleaseNotes.AppBranch;
			// Unpublished notes only for a site manager (the server sends others only published ones anyway).
			var forBranch = all
				.Where(note => canEdit || note.Published)
				.Where(note => branch == null || string.Equals(note.Branch, branch, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(note => note.Created)
				.ToList();

			notes.Children.Clear();
			var current = forBranch.FirstOrDefault(note => note.Version == ReleaseNotes.AppVersion);
			notes.Children.Add(Heading(L.T("This release")));
			if (current != null)
			{
				notes.Children.Add(NoteBlock(current));
			}
			else if (canEdit)
			{
				// None for this version yet: a site manager writes it here (a draft until published).
				notes.Children.Add(NoteBlock(new ReleaseNoteEntry(Guid.NewGuid(), "", branch ?? "", ReleaseNotes.AppVersion, false, DateTime.Now), isNew: true));
			}
			else
			{
				notes.Children.Add(new TextBlock { Text = L.T("No release notes yet…"), Opacity = 0.7 });
			}

			var previous = forBranch.Where(note => note != current).ToList();
			if (previous.Count > 0)
			{
				notes.Children.Add(Heading(branch == null ? L.T("Previous releases") : L.T("Previous releases on {0}", branch)));
				foreach (var note in previous)
				{
					notes.Children.Add(NoteBlock(note));
				}
			}
		}

		/// <summary>A release note: shown as points; for a site manager, its text and Published to change, and Save.</summary>
		private Control NoteBlock(ReleaseNoteEntry note, bool isNew = false)
		{
			if (!canEdit)
			{
				return OnboardingView.ReleaseNoteBlock(AsReleaseNote(note));
			}

			var title = new TextBlock { FontWeight = FontWeight.SemiBold };
			void ShowTitle() => title.Text = $"{note.Version} · {note.Created:d MMM yyyy}" + (note.Published ? "" : " · " + L.T("draft"));
			ShowTitle();
			var text = new TextBox
			{
				Text = note.Text,
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MinHeight = 80,
				PlaceholderText = L.T("What this release brings, one point per line"),
			};
			var published = new CheckBox { Content = L.T("Published"), IsChecked = note.Published };
			var save = new Button { Content = isNew ? L.T("Add release note") : L.T("Save"), IsEnabled = false };
			var message = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };

			void Changed() => save.IsEnabled = !string.IsNullOrWhiteSpace(text.Text) && ((text.Text ?? "") != note.Text || published.IsChecked == true != note.Published || isNew);
			text.TextChanged += (_, _) => Changed();
			published.IsCheckedChanged += (_, _) => Changed();
			save.Click += async (_, _) =>
			{
				var changed = note with { Text = (text.Text ?? "").Trim(), Published = published.IsChecked == true };
				save.IsEnabled = false;
				try
				{
					await service!.SaveReleaseNoteAsync(changed);
					note = changed;
					isNew = false;
					save.Content = L.T("Save");
					ShowTitle();
					Show(message, L.T("Saved."), error: false);
				}
				catch (Exception ex)
				{
					Show(message, L.T("Couldn't save the release note. {0}", ex.Message), error: true);
					Changed();
				}
			};

			var card = new Border
			{
				Padding = new Thickness(12),
				CornerRadius = new CornerRadius(8),
				Child = new StackPanel
				{
					Spacing = 8,
					Children =
					{
						title,
						text,
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { published, save } },
						message,
					},
				},
			};
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		/// <summary>The release notes built into the app, newest first.</summary>
		private void ShowBuiltInNotes()
		{
			notes.Children.Clear();
			notes.Children.Add(Heading(L.T("Release notes")));
			foreach (var note in ReleaseNotes.All)
			{
				notes.Children.Add(OnboardingView.ReleaseNoteBlock(note));
			}
		}

		/// <summary>A stored release note as the notes are shown: one point per line of its text; a draft (unpublished) says so.</summary>
		private static ReleaseNote AsReleaseNote(ReleaseNoteEntry note)
			=> new(
				note.Created,
				note.Published ? note.Version : L.T("{0} (draft, not published)", note.Version),
				note.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.TrimStart('-', '•', '*', ' ')).ToList());

		private static TextBlock Heading(string text)
			=> new() { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) };

		private static void Show(TextBlock message, string text, bool error)
		{
			message.Text = text;
			message.IsVisible = true;
			message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable(error ? LyriousTheme.ErrorBrush : LyriousTheme.SuccessBrush));
		}
	}
}
