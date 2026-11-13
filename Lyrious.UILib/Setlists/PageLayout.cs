using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>Building blocks shared by the setlist and lyrics pages (code-only).</summary>
	internal static class PageLayout
	{
		private const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";

		/// <summary>
		/// Raised (bubbling) by a page's ✕: the page wants to go back to the one before. The host of the
		/// pages handles it (see HomeView); a page doesn't need to know what came before it.
		/// </summary>
		public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
			RoutedEvent.Register<Control, RoutedEventArgs>("PageCloseRequested", RoutingStrategies.Bubble);

		/// <summary>
		/// A centered, width-limited column with an optional back link, a title and content.
		/// <paramref name="titleAction"/> is a control (e.g. an add button) shown at the right of the title.
		/// <paramref name="closable"/> adds a ✕ at the far right that goes back to the page before.
		/// </summary>
		public static Control Page(string title, Control content, string? backText = null, Action? back = null, string? subtitle = null, Control? titleAction = null, bool closable = false)
			=> Page(title, content, backText != null && back != null ? BackLink(backText, back) : null, subtitle, WithClose(titleAction, closable));

		/// <summary>The title's action with a ✕ after it (raising <see cref="CloseRequestedEvent"/>), when closable.</summary>
		private static Control? WithClose(Control? titleAction, bool closable)
		{
			if (!closable)
			{
				return titleAction;
			}

			var close = HomeView.IconButton(CloseIcon, "Close");
			close.Click += (_, _) => close.RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			if (titleAction != null)
			{
				titleAction.VerticalAlignment = VerticalAlignment.Center;
				actions.Children.Add(titleAction);
			}

			actions.Children.Add(close);
			return actions;
		}

		private static Control Page(string title, Control content, Control? backLink, string? subtitle = null, Control? titleAction = null)
		{
			var column = new DockPanel { MaxWidth = 640, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Stretch };

			var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
			if (backLink != null)
			{
				header.Children.Add(backLink);
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
			var (row, show) = NameRow(placeholder, add);
			addButton.Click += (_, _) => show();
			return (addButton, row);
		}

		/// <summary>A "+" button for a page title (e.g. to open a menu of ways to add something).</summary>
		public static Button AddButton(string description) => HomeView.IconButton(AddIcon, description);

		/// <summary>
		/// A hidden row with a name box, Add and Cancel; <c>Show</c> reveals it and focuses the box.
		/// Add (or Enter) passes the trimmed name to <paramref name="add"/>; Cancel (or Escape) hides it.
		/// Dock the row at the top of the page content.
		/// </summary>
		public static (Control Row, Action Show) NameRow(string placeholder, Action<string> add)
		{
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

			void Show()
			{
				row.IsVisible = true;
				nameBox.Focus();
			}

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
			return (row, Show);
		}

		/// <summary>A back link for a page header, e.g. "‹ Setlist 1".</summary>
		private static HyperlinkButton BackLink(string text, Action back)
		{
			var link = new HyperlinkButton { Content = text, Padding = new Thickness(0) };
			link.Click += (_, _) => back();
			return link;
		}

		/// <summary>A full-width, left-aligned button for one list row (easy to tap on phones).</summary>
		public static Button ListRow(string text, string? detail, Action onClick)
		{
			var button = new Button
			{
				Content = RowContent(text, detail),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(16, 12),
			};
			button.Click += (_, _) => onClick();
			return button;
		}

		/// <summary>A list row's text, with an optional detail at the right (e.g. key, tempo and beat).</summary>
		public static Control RowContent(string text, string? detail)
		{
			var row = new DockPanel();
			if (!string.IsNullOrEmpty(detail))
			{
				var detailText = new TextBlock { Text = detail, Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
				DockPanel.SetDock(detailText, Dock.Right);
				row.Children.Add(detailText);
			}

			row.Children.Add(new TextBlock { Text = text, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
			return row;
		}

		// An arrow into a box's opening: "open this".
		private const string OpenIcon = "M9,6 L15,12 L9,18";

		// A trash can: lid, handle, can and two lines on it.
		private const string TrashIcon = "M5,7 H19 M10,7 V5 H14 V7 M7,7 L8,20 H16 L17,7 M10,11 V16 M14,11 V16";

		// A pencil: the body, its tip, and the line where the tip meets the body.
		private const string EditIcon = "M4,20 L8,19 L19,8 L16,5 L5,16 Z M14,7 L17,10";

		/// <summary>A toolbar button that edits the selected item.</summary>
		public static Button EditButton(string description, Action edit)
		{
			var button = HomeView.IconButton(EditIcon, description);
			button.Click += (_, _) => edit();
			return button;
		}

		/// <summary>A toolbar button that opens (makes current) the selected item, like a double click does.</summary>
		public static Button OpenButton(string description, Action open)
		{
			var button = HomeView.IconButton(OpenIcon, description);
			button.Click += (_, _) => open();
			return button;
		}

		/// <summary>A trash button that asks first, in a small popup with <paramref name="question"/>, Delete and Cancel.</summary>
		public static Button DeleteButton(string description, string question, Action delete)
		{
			var button = HomeView.IconButton(TrashIcon, description);

			var confirm = new Button { Content = "Delete", Foreground = Brushes.White };
			confirm.Bind(Button.BackgroundProperty, confirm.GetResourceObservable(LyriousTheme.ErrorBrush));
			var cancel = new Button { Content = "Cancel" };
			var flyout = new Flyout
			{
				Content = new StackPanel
				{
					Spacing = 12,
					MaxWidth = 280,
					Children =
					{
						new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap },
						new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } },
					},
				},
			};
			confirm.Click += (_, _) =>
			{
				flyout.Hide();
				delete();
			};
			cancel.Click += (_, _) => flyout.Hide();
			button.Flyout = flyout;
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
