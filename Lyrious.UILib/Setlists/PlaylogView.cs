using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The band's sessions (playlogs): at the top the latest one with the songs played in it (time,
	/// song, tempo, key, length and conductor's initials); below, all sessions, newest first, each by
	/// its date, its number of songs and the setlists used. Tapping one there shows it at the top instead;
	/// the one shown is highlighted.
	/// </summary>
	public sealed class PlaylogView : UserControl
	{
		private readonly IReadOnlyList<PlaylogSummary> playlogs;
		private readonly StackPanel shown = new() { Spacing = 8 };
		private readonly TextBlock shownTitle = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 0) };
		// The date and time after the title: smaller and dimmed, on the title's baseline (the
		// smaller text sits a little less deep below it).
		private readonly TextBlock shownDate = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
		private readonly List<(PlaylogSummary Playlog, Button Row)> rows = [];

		/// <param name="setlistsUsed">The setlists used in each session (by its id), as far as is known.</param>
		public PlaylogView(GroupSummary group, IReadOnlyList<PlaylogSummary> playlogs, IReadOnlyDictionary<Guid, IReadOnlyList<string>>? setlistsUsed = null)
		{
			this.playlogs = playlogs;

			Control body;
			if (playlogs.Count == 0)
			{
				body = new TextBlock { Text = L.T("Nothing has been played yet."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
			}
			else
			{
				var list = new StackPanel { Spacing = 8 };
				foreach (var playlog in playlogs)
				{
					// The date (compact, no time), and the number of songs with the setlists used.
					var detail = Count(playlog);
					if (setlistsUsed?.GetValueOrDefault(playlog.Id) is { Count: > 0 } setlists)
					{
						detail += " · " + string.Join(", ", setlists);
					}

					var row = PageLayout.ListRow(CompactDate(playlog.CreatedAt), detail, () => Show(playlog));
					rows.Add((playlog, row));
					list.Children.Add(row);
				}

				body = new ScrollViewer
				{
					Content = new StackPanel
					{
						Spacing = 8,
						Children =
						{
							new WrapPanel { Orientation = Orientation.Horizontal, Children = { shownTitle, shownDate } },
							shown,
							Section(L.T("All sessions")),
							list,
						},
					},
				};
				Show(playlogs[0]);
			}

			Content = PageLayout.Page(L.T("Sessions"), body, closable: true, icon: AppIcons.Playlog);
		}

		/// <summary>Shows the playlog's songs at the top, and highlights its row in the list.</summary>
		private void Show(PlaylogSummary playlog)
		{
			shownTitle.Text = ReferenceEquals(playlog, playlogs[0]) ? L.T("Last session") : L.T("Session");
			shownDate.Text = Date(playlog.CreatedAt);
			shown.Children.Clear();
			if (playlog.Plays.Count == 0)
			{
				shown.Children.Add(new TextBlock { Text = L.T("No songs in this session."), Opacity = 0.7 });
			}

			// Up and down go through the songs played (see ListKeys).
			if (ListKeys.GetStepper(shown) == null)
			{
				ListKeys.Stepped(shown);
			}

			foreach (var play in playlog.Plays)
			{
				shown.Children.Add(PlayRow(play));
			}

			foreach (var (rowPlaylog, row) in rows)
			{
				if (ReferenceEquals(rowPlaylog, playlog))
				{
					row.Classes.Add("accent");
				}
				else
				{
					row.Classes.Remove("accent");
				}
			}
		}

		/// <summary>One song played: the song, then tempo · key · length · conductor's initials.</summary>
		private static Control PlayRow(PlayEntry play)
		{
			var content = PageLayout.RowContent(play.SongName, Details(play));
			// As high as a row with the play button (its 18 px icon and 8 px around it), with or without one.
			var row = new DockPanel { MinHeight = 34 };
			// The band's recording of the play (when a device recorded it): played here, fetched from the
			// server first when it isn't on this device, with how far it has played along the bottom.
			SoundItemPlayer? player = null;
			if (MediaLinks.SoundOf(play.Recording) is { } recording && App.SoundStore is { } sounds)
			{
				// (Not on this device: a download button first; the play still fetches it if it's gone meanwhile.)
				player = new SoundItemPlayer(sounds, recording.Id, L.T("Play the band's recording of it"),
					async () => await sounds.PlayAsync(recording.Id) || (await SoundSync.FetchAsync(recording.Id) && await sounds.PlayAsync(recording.Id)),
					needsDownload: null);
				DockPanel.SetDock(player.Button, Dock.Right);
				row.Children.Add(player.Button);
			}

			row.Children.Add(content);
			// Room at the left, so the song name isn't against the edge, and at the right for the conductor's
			// initials (the play button, when there is one, is at the edge): the list stays compact.
			var padding = new Thickness(16, 0, player == null ? 16 : 0, 0);
			var card = player?.Card(row, padding) ?? new Border { Padding = padding, Child = row };
			card.CornerRadius = new CornerRadius(6);
			ListKeys.SetIsItem(card, true);
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		private static string Details(PlayEntry play)
		{
			// Tempo before key, as song details go everywhere.
			var parts = new List<string>();
			if (play.Tempo > 0)
			{
				parts.Add($"{play.Tempo.ToString("0.#", CultureInfo.InvariantCulture)} bpm");
			}

			if (!string.IsNullOrWhiteSpace(play.Key))
			{
				parts.Add(play.Key);
			}

			if (play.Duration > TimeSpan.Zero)
			{
				parts.Add(play.Duration.ToString(play.Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture));
			}

			if (!string.IsNullOrWhiteSpace(play.Conductor))
			{
				parts.Add(Initials(play.Conductor));
			}

			return string.Join(" · ", parts);
		}

		/// <summary>A name's initials, e.g. "Robert Westman" → "RW".</summary>
		private static string Initials(string name) =>
			string.Concat(name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpper(part[0], CultureInfo.CurrentCulture)));

		private static string Date(DateTime when) => $"{when:D} {when:t}";

		/// <summary>A date in short: "20 Nov" this year, else "20 Nov 2025" (no time).</summary>
		private static string CompactDate(DateTime when)
			=> when.ToString(when.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);

		private static string Count(PlaylogSummary playlog) => playlog.Plays.Count == 1 ? L.T("1 song") : L.T("{0} songs", playlog.Plays.Count);

		private static TextBlock Section(string title) => new()
		{
			Text = title,
			FontSize = 20,
			FontWeight = FontWeight.SemiBold,
			Margin = new Thickness(0, 16, 0, 0),
		};
	}
}
