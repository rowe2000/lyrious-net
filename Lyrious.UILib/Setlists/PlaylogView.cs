using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The band's playlogs: at the top the latest one with the songs played in it (time, song, key,
	/// tempo, length and conductor's initials); below, all playlogs, newest first. Tapping one there shows it at the
	/// top instead; the one shown is highlighted.
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

		public PlaylogView(GroupSummary group, IReadOnlyList<PlaylogSummary> playlogs)
		{
			this.playlogs = playlogs;

			Control body;
			if (playlogs.Count == 0)
			{
				body = new TextBlock { Text = "Nothing has been played yet.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
			}
			else
			{
				var list = new StackPanel { Spacing = 8 };
				foreach (var playlog in playlogs)
				{
					var row = PageLayout.ListRow(Date(playlog.CreatedAt), Count(playlog), () => Show(playlog));
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
							Section("All playlogs"),
							list,
						},
					},
				};
				Show(playlogs[0]);
			}

			Content = PageLayout.Page("Playlog", body, subtitle: $"{group.Name} · songs played", closable: true);
		}

		/// <summary>Shows the playlog's songs at the top, and highlights its row in the list.</summary>
		private void Show(PlaylogSummary playlog)
		{
			shownTitle.Text = ReferenceEquals(playlog, playlogs[0]) ? "Last playlog" : "Playlog";
			shownDate.Text = Date(playlog.CreatedAt);
			shown.Children.Clear();
			if (playlog.Plays.Count == 0)
			{
				shown.Children.Add(new TextBlock { Text = "No songs in this playlog.", Opacity = 0.7 });
			}

			foreach (var (play, number) in playlog.Plays.Select((play, index) => (play, index + 1)))
			{
				shown.Children.Add(PlayRow(play, number));
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

		/// <summary>One song played: its number and time at the left, the song, then key · tempo · length · conductor's initials.</summary>
		private static Control PlayRow(PlayEntry play, int number)
		{
			var when = new TextBlock
			{
				Text = $"{number}.  {play.PlayedAt:t}",
				Opacity = 0.7,
				MinWidth = 90,
				VerticalAlignment = VerticalAlignment.Center,
			};
			DockPanel.SetDock(when, Dock.Left);

			var content = PageLayout.RowContent(play.SongName, Details(play));
			var row = new DockPanel { Children = { when, content } };
			var card = new Border { Padding = new Thickness(16, 10), CornerRadius = new CornerRadius(6), Child = row };
			card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LyriousTheme.CardBrush));
			return card;
		}

		private static string Details(PlayEntry play)
		{
			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(play.Key))
			{
				parts.Add(play.Key);
			}

			if (play.Tempo > 0)
			{
				parts.Add($"{play.Tempo.ToString("0.#", CultureInfo.InvariantCulture)} bpm");
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

		private static string Count(PlaylogSummary playlog) => playlog.Plays.Count == 1 ? "1 song" : $"{playlog.Plays.Count} songs";

		private static TextBlock Section(string title) => new()
		{
			Text = title,
			FontSize = 20,
			FontWeight = FontWeight.SemiBold,
			Margin = new Thickness(0, 16, 0, 0),
		};
	}
}
