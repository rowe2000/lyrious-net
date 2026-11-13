using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>Building blocks shared by the setlist and lyrics pages (code-only).</summary>
	internal static class PageLayout
	{
		/// <summary>
		/// A centered, width-limited column with an optional back link, a title and content.
		/// <paramref name="titleAction"/> is a control (e.g. an add button) shown at the right of the title.
		/// </summary>
		public static Control Page(string title, Control content, string? backText = null, Action? back = null, string? subtitle = null, Control? titleAction = null)
		{
			var column = new DockPanel { MaxWidth = 640, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Stretch };

			var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
			if (backText != null && back != null)
			{
				var link = new HyperlinkButton { Content = backText, Padding = new Thickness(0) };
				link.Click += (_, _) => back();
				header.Children.Add(link);
			}

			var titleText = new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
			if (titleAction != null)
			{
				titleAction.VerticalAlignment = VerticalAlignment.Center;
				DockPanel.SetDock(titleAction, Dock.Right);
				header.Children.Add(new DockPanel { Children = { titleAction, titleText } });
			}
			else
			{
				header.Children.Add(titleText);
			}

			if (!string.IsNullOrEmpty(subtitle))
			{
				header.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
			}

			DockPanel.SetDock(header, Dock.Top);
			column.Children.Add(header);
			column.Children.Add(content);
			return column;
		}

		private const string AddIcon = "M12,5 V19 M5,12 H19";

		/// <summary>
		/// A "+" button for a page title and the row it reveals: a name box with Add and Cancel.
		/// Add (or Enter) passes the trimmed name to <paramref name="add"/>; Cancel (or Escape) hides
		/// the row again. Dock the row at the top of the page content.
		/// </summary>
		public static (Button AddButton, Control NameRow) AddByName(string description, string placeholder, Action<string> add)
		{
			var addButton = HomeView.IconButton(AddIcon, description);
			var nameBox = new TextBox { PlaceholderText = placeholder };
			var confirm = new Button { Content = "Add", IsEnabled = false };
			confirm.Classes.Add("accent");
			var cancel = new Button { Content = "Cancel" };

			var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 0), Children = { confirm, cancel } };
			DockPanel.SetDock(buttons, Dock.Right);
			var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16), IsVisible = false, Children = { buttons, nameBox } };
			DockPanel.SetDock(row, Dock.Top);

			void Close()
			{
				row.IsVisible = false;
				nameBox.Text = string.Empty;
			}

			void Submit()
			{
				var name = nameBox.Text?.Trim();
				if (!string.IsNullOrEmpty(name))
				{
					add(name);
				}
			}

			addButton.Click += (_, _) =>
			{
				row.IsVisible = true;
				nameBox.Focus();
			};
			nameBox.TextChanged += (_, _) => confirm.IsEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
			nameBox.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Enter)
				{
					Submit();
					e.Handled = true;
				}
				else if (e.Key == Key.Escape)
				{
					Close();
					e.Handled = true;
				}
			};
			confirm.Click += (_, _) => Submit();
			cancel.Click += (_, _) => Close();
			return (addButton, row);
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

		/// <summary>A song's key, tempo and beat, e.g. "G# · 120 bpm · 4/4"; empty if none are set.</summary>
		public static string SongInfo(SongDetails song)
		{
			var parts = new System.Collections.Generic.List<string>();
			if (!string.IsNullOrWhiteSpace(song.Key))
			{
				parts.Add(song.Key);
			}

			if (song.Tempo > 0)
			{
				parts.Add($"{song.Tempo.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} bpm");
			}

			if (!string.IsNullOrWhiteSpace(song.Beat))
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
