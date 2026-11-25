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
	/// app was built from, as a list of foldable releases: this release's first and open, then the
	/// previous ones, newest first and folded. Others see only published notes; a site manager also
	/// sees unpublished ones (drafts), publishes or unpublishes each with a check box (saved at once),
	/// edits its text with Edit (Save, Cancel), and writes this release's note when there's none yet.
	/// Without the server, the notes built into the app (<see cref="ReleaseNotes.All"/>), likewise.
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
		/// its branch), as one list of foldable releases: this release's first (its version), open, then
		/// the previous ones, newest first, folded.
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
			notes.Children.Add(Heading(branch == null ? L.T("Release notes") : L.T("Release notes on {0}", branch)));
			var current = forBranch.FirstOrDefault(note => note.Version == ReleaseNotes.AppVersion);
			if (current != null)
			{
				notes.Children.Add(NoteItem(current, open: true));
			}
			else if (canEdit)
			{
				// None for this version yet: a site manager writes it here (a draft until published).
				notes.Children.Add(NoteItem(new ReleaseNoteEntry(Guid.NewGuid(), "", branch ?? "", ReleaseNotes.AppVersion, false, DateTime.Now), open: true, isNew: true));
			}
			else
			{
				notes.Children.Add(new TextBlock { Text = L.T("No release notes for this release yet…"), Opacity = 0.7 });
			}

			foreach (var note in forBranch.Where(note => note != current))
			{
				notes.Children.Add(NoteItem(note, open: false));
			}
		}

		/// <summary>
		/// A release, foldable: its version and date (and "draft" when unpublished) as the heading, its
		/// points under it. For a site manager also Published (saved as soon as it's changed) and Edit,
		/// which turns the points into the note's text to change, with Save and Cancel.
		/// </summary>
		/// <param name="isNew">This release's note, not written yet: it opens to be written, and is added by saving it.</param>
		private Control NoteItem(ReleaseNoteEntry note, bool open, bool isNew = false)
		{
			var body = new StackPanel { Spacing = 8 };
			var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
			void ShowTitle() => title.Text = $"{note.Version} · {note.Created:d MMM yyyy}" + (note.Published ? "" : " · " + L.T("draft"));
			ShowTitle();
			var item = Foldable(title, body, open);
			if (!canEdit)
			{
				body.Children.Add(Points(note));
				return item;
			}

			var points = new ContentControl();
			var message = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			var published = new CheckBox { Content = L.T("Published"), IsChecked = note.Published, IsEnabled = !isNew };
			ToolTip.SetTip(published, L.T("Published notes show to everyone; a draft only to site managers"));
			var edit = new Button { Content = L.T("Edit") };
			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { published, edit } };
			body.Children.Add(points);
			body.Children.Add(actions);
			body.Children.Add(message);

			// Published or not: saved at once (back as it was if that fails).
			var settingPublished = false;
			published.IsCheckedChanged += async (_, _) =>
			{
				if (settingPublished || published.IsChecked == true == note.Published)
				{
					return;
				}

				var changed = note with { Published = published.IsChecked == true };
				published.IsEnabled = false;
				try
				{
					await service!.SaveReleaseNoteAsync(changed);
					note = changed;
					ShowTitle();
					message.IsVisible = false;
				}
				catch (Exception ex)
				{
					settingPublished = true;
					published.IsChecked = note.Published;
					settingPublished = false;
					Show(message, L.T("Couldn't save the release note. {0}", ex.Message), error: true);
				}
				finally
				{
					published.IsEnabled = true;
				}
			};

			// The note's points, as everyone sees them.
			void ShowPoints()
			{
				points.Content = string.IsNullOrWhiteSpace(note.Text)
					? new TextBlock { Text = L.T("No text yet."), Opacity = 0.7 }
					: Points(note);
				actions.IsVisible = true;
			}

			// The note's text to change, with Save (once it's changed) and Cancel.
			void ShowEditor()
			{
				var text = new TextBox
				{
					Text = note.Text,
					AcceptsReturn = true,
					TextWrapping = TextWrapping.Wrap,
					MinHeight = 80,
					PlaceholderText = L.T("What this release brings, one point per line"),
				};
				var save = new Button { Content = isNew ? L.T("Add release note") : L.T("Save"), IsEnabled = false };
				save.Classes.Add("accent");
				var cancel = new Button { Content = L.T("Cancel"), IsVisible = !isNew };
				text.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(text.Text) && (isNew || (text.Text ?? "").Trim() != note.Text);
				cancel.Click += (_, _) => ShowPoints();
				save.Click += async (_, _) =>
				{
					var changed = note with { Text = (text.Text ?? "").Trim() };
					save.IsEnabled = false;
					try
					{
						await service!.SaveReleaseNoteAsync(changed);
						note = changed;
						isNew = false;
						published.IsEnabled = true;
						ShowPoints();
						Show(message, L.T("Saved."), error: false);
					}
					catch (Exception ex)
					{
						Show(message, L.T("Couldn't save the release note. {0}", ex.Message), error: true);
						save.IsEnabled = true;
					}
				};

				points.Content = new StackPanel
				{
					Spacing = 8,
					Children = { text, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } } },
				};
				actions.IsVisible = false;
				message.IsVisible = false;
			}

			edit.Click += (_, _) => ShowEditor();
			if (isNew)
			{
				ShowEditor();
			}
			else
			{
				ShowPoints();
			}

			return item;
		}

		/// <summary>
		/// A foldable item in a card: <paramref name="heading"/> after a chevron, as a button that opens and
		/// closes <paramref name="body"/> under it.
		/// </summary>
		private static Control Foldable(Control heading, Control body, bool open)
		{
			var chevron = AppIcons.Create(AppIcons.Chevron, 16);
			var toggle = new Button
			{
				Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { chevron, heading } },
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Padding = new Thickness(0, 2),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
			};
			body.Margin = new Thickness(24, 8, 0, 0);

			void Show()
			{
				body.IsVisible = open;
				chevron.RenderTransform = new RotateTransform(open ? 90 : 0);
			}

			toggle.Click += (_, _) =>
			{
				open = !open;
				Show();
			};
			Show();

			var card = new Border
			{
				Padding = new Thickness(12, 8),
				CornerRadius = new CornerRadius(8),
				Child = new StackPanel { Children = { toggle, body } },
			};
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		/// <summary>A stored release note's points: one per line of its text (without a leading "-", "•" or "*").</summary>
		private static Control Points(ReleaseNoteEntry note)
			=> OnboardingView.Bullets(note.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.TrimStart('-', '•', '*', ' ')).ToList());

		/// <summary>The release notes built into the app, newest first, foldable: the newest open.</summary>
		private void ShowBuiltInNotes()
		{
			notes.Children.Clear();
			notes.Children.Add(Heading(L.T("Release notes")));
			var first = true;
			foreach (var note in ReleaseNotes.All)
			{
				var title = new TextBlock { Text = $"{note.Version} · {note.Date:d MMM yyyy}", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
				notes.Children.Add(Foldable(title, OnboardingView.Bullets(note.Notes), first));
				first = false;
			}
		}

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
