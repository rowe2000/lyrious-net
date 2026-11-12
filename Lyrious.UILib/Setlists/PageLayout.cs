using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>Building blocks shared by the setlist and lyrics pages (code-only).</summary>
	internal static class PageLayout
	{
		/// <summary>A centered, width-limited column with an optional back link, a title and content.</summary>
		public static Control Page(string title, Control content, string? backText = null, Action? back = null, string? subtitle = null)
		{
			var column = new DockPanel { MaxWidth = 640, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Stretch };

			var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
			if (backText != null && back != null)
			{
				var link = new HyperlinkButton { Content = backText, Padding = new Thickness(0) };
				link.Click += (_, _) => back();
				header.Children.Add(link);
			}

			header.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
			if (!string.IsNullOrEmpty(subtitle))
			{
				header.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
			}

			DockPanel.SetDock(header, Dock.Top);
			column.Children.Add(header);
			column.Children.Add(content);
			return column;
		}

		/// <summary>A full-width, left-aligned button for one list row (easy to tap on phones).</summary>
		public static Button ListRow(string text, string? detail, Action onClick)
		{
			var row = new DockPanel();
			if (!string.IsNullOrEmpty(detail))
			{
				var detailText = new TextBlock { Text = detail, Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
				DockPanel.SetDock(detailText, Dock.Right);
				row.Children.Add(detailText);
			}

			row.Children.Add(new TextBlock { Text = text, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

			var button = new Button
			{
				Content = row,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 12),
			};
			button.Click += (_, _) => onClick();
			return button;
		}

		/// <summary>A song's key, tempo and (optionally) beat, e.g. "Key G# · 120 bpm · 4/4"; empty if none are set.</summary>
		public static string SongInfo(SongDetails song, bool includeBeat = true)
		{
			var parts = new System.Collections.Generic.List<string>();
			if (!string.IsNullOrWhiteSpace(song.Key))
			{
				parts.Add($"Key {song.Key}");
			}

			if (song.Tempo > 0)
			{
				parts.Add($"{song.Tempo.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} bpm");
			}

			if (includeBeat && !string.IsNullOrWhiteSpace(song.Beat))
			{
				parts.Add(song.Beat);
			}

			return string.Join(" · ", parts);
		}

		public static Control List(Control[] rows, string emptyText)
		{
			if (rows.Length == 0)
			{
				return new TextBlock { Text = emptyText, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
			}

			var list = new StackPanel { Spacing = 8 };
			list.Children.AddRange(rows);
			return new ScrollViewer { Content = list };
		}
	}
}
