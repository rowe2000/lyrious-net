using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Shown after login. The first time on a device: a short introduction to the app. After that:
	/// what changed in the member's bands since their last visit (bands they were added to, new
	/// members, new and changed setlists and songs) and the app's release notes since then.
	/// Continue (or ✕) goes on to the start page.
	/// <para>
	/// Bands the member is invited to come first, each with an Accept button that stands out (it joins
	/// the band); Continue is then a plain button.
	/// </para>
	/// </summary>
	public sealed class OnboardingView : UserControl
	{
		/// <summary>A band the member is invited to, the names of its members, and the member's own name in it (added to them once accepted).</summary>
		public sealed record Invitation(GroupSummary Band, IReadOnlyList<string> MemberNames, string MyName);

		/// <summary>Raised when the member goes on (Continue).</summary>
		public event Action? Continued;

		/// <param name="lastVisit">When the member last saw this screen on this device; null the first time.</param>
		/// <param name="changes">The changes since then; null when they couldn't be found out (e.g. offline).</param>
		/// <param name="invitations">Bands the member is invited to, shown first.</param>
		/// <param name="accept">Accepts an invitation (joins the band).</param>
		public OnboardingView(string memberName, DateTime? lastVisit, IReadOnlyList<Change>? changes,
			IReadOnlyList<Invitation>? invitations = null, Func<GroupSummary, System.Threading.Tasks.Task>? accept = null)
		{
			var column = new StackPanel { Spacing = 12 };
			var invited = invitations is { Count: > 0 } && accept != null;
			if (invited)
			{
				foreach (var invitation in invitations!)
				{
					column.Children.Add(InvitationCard(invitation, accept!));
				}
			}

			string title;
			if (lastVisit is not { } since)
			{
				title = L.T("Welcome to Lyrious");
				column.Children.Add(Paragraph(L.T("Hi {0}! Lyrious keeps your band's songs, setlists and lyrics in one place, and on every member's screen at once.", memberName)));
				column.Children.Add(Bullets(
				[
					L.T("Bands: join or start bands, invite members, and pick the band you play with now from the menu."),
					L.T("Your private band: a place of your own for songs and setlists only you can see."),
					L.T("Setlists and songbook: put the band's songs in setlists for each gig; the songbook holds them all."),
					L.T("Lyrics that scroll in time: time tags such as [0:12], or a '|' for each bar, keep the lyrics in step with the song; the grid button shows the chords in bar columns."),
					L.T("Band sync (the band icon): follow the song the band is on, and play and stop together."),
					L.T("Media: link a song to Spotify, YouTube or a sound file, and play it from the lyrics page."),
				]));
				column.Children.Add(Paragraph(L.T("The menu at the top left has your bands, setlists and songbook; the list at the top right is the current setlist.")));
			}
			else
			{
				title = L.T("Since your last visit");
				column.Children.Add(new TextBlock { Text = L.T("Last visit {0}", since.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)), Opacity = 0.7 });
				if (changes == null)
				{
					column.Children.Add(Paragraph(L.T("Couldn't find out what changed (the server couldn't be reached).")));
				}
				else if (changes.Count == 0)
				{
					column.Children.Add(Paragraph(L.T("Nothing new since your last visit.")));
				}
				else
				{
					// By band, the band with the latest change first.
					foreach (var band in changes.GroupBy(change => change.Band))
					{
						column.Children.Add(Heading(band.Key.Length > 0 ? band.Key : L.T("Other")));
						column.Children.Add(Bullets(band.Select(Describe).ToList()));
					}
				}

				var notes = ReleaseNotes.Since(since);
				if (notes.Count > 0)
				{
					column.Children.Add(Heading(L.T("What's new in Lyrious")));
					foreach (var note in notes)
					{
						column.Children.Add(ReleaseNoteBlock(note));
					}
				}
			}

			// The invitation's Accept is what stands out then; Continue is plain.
			var go = new Button { Content = L.T("Continue"), Height = 40, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
			if (!invited)
			{
				go.Classes.Add("accent");
			}

			go.Click += (_, _) => Continued?.Invoke();
			column.Children.Add(go);

			Content = PageLayout.Page(
				invited ? L.T("You're invited") : title,
				new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				closable: true);
		}

		/// <summary>
		/// A band the member is invited to: its name, its members, and Accept (large, highlighted), which
		/// joins it; after that, that they're in the band, with themselves among its members. What went
		/// wrong shows below Accept.
		/// </summary>
		private static Control InvitationCard(Invitation invitation, Func<GroupSummary, System.Threading.Tasks.Task> accept)
		{
			var heading = new TextBlock { Text = L.T("You're invited to {0}", invitation.Band.Name), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
			var members = new TextBlock
			{
				Text = invitation.MemberNames.Count > 0 ? L.T("Members: {0}", string.Join(", ", invitation.MemberNames)) : L.T("Join the band to share its songs and setlists."),
				Opacity = 0.75,
				TextWrapping = TextWrapping.Wrap,
			};
			var acceptButton = new Button
			{
				Content = L.T("Accept"),
				Classes = { "accent" },
				FontSize = 18,
				Height = 48,
				MinWidth = 160,
				HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
				VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
				Margin = new Thickness(0, 4, 0, 0),
			};
			var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable(LyriousTheme.ErrorBrush));
			acceptButton.Click += async (_, _) =>
			{
				acceptButton.IsEnabled = false;
				problem.IsVisible = false;
				try
				{
					await accept(invitation.Band);
					heading.Text = L.T("You're now in {0}", invitation.Band.Name);
					// Now one of them: the member is in the list too.
					members.Text = L.T("Members: {0}", string.Join(", ", invitation.MemberNames.Append(L.T("{0} (you)", invitation.MyName))));
					acceptButton.IsVisible = false;
				}
				catch (Exception ex)
				{
					problem.Text = L.T("Couldn't accept the invitation. {0}", ex.Message);
					problem.IsVisible = true;
					acceptButton.IsEnabled = true;
				}
			};

			var card = new Border
			{
				Padding = new Thickness(16),
				CornerRadius = new CornerRadius(8),
				BorderThickness = new Thickness(2),
				Child = new StackPanel { Spacing = 8, Children = { heading, members, acceptButton, problem } },
			};
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			card.Bind(Border.BorderBrushProperty, card.GetResourceObservable(LyriousTheme.SelectionBrush));
			return card;
		}

		/// <summary>E.g. "New song: Blackbird", "Changed setlist: Friday", "New member: Anna", "You were added to the band".</summary>
		private static string Describe(Change change) => change.Kind switch
		{
			ChangeKind.Band => L.T("You were added to the band {0}", change.Name),
			ChangeKind.Member => L.T("New member: {0}", change.Name),
			ChangeKind.Setlist => change.IsNew ? L.T("New setlist: {0}", change.Name) : L.T("Changed setlist: {0}", change.Name),
			_ => change.IsNew ? L.T("New song: {0}", change.Name) : L.T("Changed song: {0}", change.Name),
		};

		/// <summary>A release's version and date, and its notes as bullets.</summary>
		internal static Control ReleaseNoteBlock(ReleaseNote note) => new StackPanel
		{
			Spacing = 6,
			Children =
			{
				new TextBlock { Text = $"{note.Version} · {note.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}", FontWeight = FontWeight.SemiBold },
				Bullets(note.Notes),
			},
		};

		private static TextBlock Heading(string text)
			=> new() { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };

		private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

		/// <summary>Lines as a list of points, each with a dot before it.</summary>
		internal static Control Bullets(IReadOnlyList<string> lines)
		{
			var list = new StackPanel { Spacing = 4 };
			foreach (var line in lines)
			{
				var dot = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0) };
				DockPanel.SetDock(dot, Dock.Left);
				list.Children.Add(new DockPanel { Children = { dot, Paragraph(line) } });
			}

			return list;
		}
	}
}
