using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>Building blocks shared by the setlist and lyrics pages (code-only).</summary>
	internal static class PageLayout
	{
		private const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";

		// An icon button's height (a 24 icon with 8 around it): the drawer buttons in the top corners.
		private const double DrawerButtonSize = 40;

		/// <summary>
		/// Raised (bubbling) by a page's ✕: the page wants to go back to the one before. The host of the
		/// pages handles it (see HomeView); a page doesn't need to know what came before it.
		/// </summary>
		public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
			RoutedEvent.Register<Control, RoutedEventArgs>("PageCloseRequested", RoutingStrategies.Bubble);

		/// <summary>
		/// A page as wide as the app, with an optional back link, a title and content.
		/// <paramref name="titleAction"/> is a control (e.g. an add button) shown at the right of the title.
		/// <paramref name="closable"/> adds a ✕ at the far right that goes back to the page before.
		/// </summary>
		/// <paramref name="icon"/> (see <see cref="AppIcons"/>) is drawn before the title, as on the left drawer's item for the page.
		/// <param name="previous">Shows the previous of something (e.g. the previous search result): a button after the back link; null when there's none.</param>
		/// <param name="next">Shows the next of it: a button after that; null when there's none. Neither given, neither button shows.</param>
		/// <param name="place">Which one this is (from 1) of how many, shown between the previous and next buttons, e.g. "1 (12)".</param>
		public static Control Page(string title, Control content, string? backText = null, Action? back = null, string? subtitle = null, Control? titleAction = null, bool closable = false, string? icon = null,
			Action? previous = null, Action? next = null, (int Number, int Count)? place = null)
			=> Page(title, content, HeaderLinks(backText, back, previous, next, place), subtitle, WithClose(titleAction, closable), icon);

		/// <summary>
		/// What goes above a page's title: the back link, then the previous and next buttons (icons only,
		/// the one there's nothing for disabled) with which one this is between them; null when there's
		/// none of them.
		/// </summary>
		private static Control? HeaderLinks(string? backText, Action? back, Action? previous, Action? next, (int Number, int Count)? place)
		{
			var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			if (backText != null && back != null)
			{
				var backLink = HeaderLink(backText, back);
				backLink.Margin = new Thickness(0, 0, 16, 0);
				backLink.VerticalAlignment = VerticalAlignment.Center;
				links.Children.Add(backLink);
			}

			if (previous != null || next != null)
			{
				foreach (var (step, iconData, description) in new[] { (previous, PreviousIcon, L.T("Previous")), (next, NextIcon, L.T("Next")) })
				{
					var button = HomeView.IconButton(iconData, description);
					button.IsEnabled = step != null;
					button.Click += (_, _) => step?.Invoke();
					links.Children.Add(button);
					// Between the two: which one this is, of how many.
					if (step == previous && place is { } shown)
					{
						links.Children.Add(new TextBlock
						{
							Text = L.T("{0} ({1})", shown.Number, shown.Count),
							VerticalAlignment = VerticalAlignment.Center,
							Margin = new Thickness(4, 0),
						});
					}
				}
			}

			return links.Children.Count > 0 ? links : null;
		}

		/// <summary>The title's action with a ✕ after it (raising <see cref="CloseRequestedEvent"/>), when closable.</summary>
		private static Control? WithClose(Control? titleAction, bool closable)
		{
			if (!closable)
			{
				return titleAction;
			}

			var close = EscapeKey.MarkAsClose(HomeView.IconButton(CloseIcon, L.T("Close")));
			close.Click += (_, _) => close.RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			if (titleAction != null)
			{
				titleAction.VerticalAlignment = VerticalAlignment.Top;
				actions.Children.Add(titleAction);
			}

			actions.Children.Add(close);
			return actions;
		}

		/// <summary>
		/// The page's first row holds its actions (✕ and the like) at the right, top aligned and 4 from
		/// the top: they line up with the drawer buttons in the top corners (all 40 high, centred 24
		/// from the top). The title shares that row; with a back link, the link does and the title
		/// comes under it.
		/// </summary>
		// Marks a page's title (with its icon), so something can take its place (see ShowInPlaceOfTitle).
		private const string TitleTag = "page-title";

		/// <summary>
		/// Shows <paramref name="replacement"/> (e.g. a search box) in the place of a page's title, the
		/// tools at its right staying (<paramref name="shown"/> true), or the title again (false).
		/// </summary>
		public static void ShowInPlaceOfTitle(Control page, Control replacement, bool shown)
		{
			var title = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Control>(page.GetLogicalDescendants()), control => TitleTag.Equals(control.Tag));
			if (title?.Parent is not Panel row)
			{
				return;
			}

			// After the title: in a row of the header it's the last child, so it takes the room left of the tools.
			if (replacement.Parent == null)
			{
				row.Children.Add(replacement);
			}

			replacement.IsVisible = shown;
			title.IsVisible = !shown;
		}

		// Marks a page's subtitle, so it can be found to change it (see Subtitle).
		private const string SubtitleTag = "page-subtitle";

		/// <summary>The line under a page's title (e.g. "45 songs"), to change it for a while; null if the page has none.</summary>
		public static TextBlock? Subtitle(Control page)
			=> System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<TextBlock>(page.GetLogicalDescendants()), text => SubtitleTag.Equals(text.Tag));

		private static Control Page(string title, Control content, Control? backLink, string? subtitle = null, Control? titleAction = null, string? icon = null)
		{
			// The whole width of the app (as the song pages), not a narrow column in the middle.
			var column = new DockPanel { Margin = new Thickness(ColumnMargin), HorizontalAlignment = HorizontalAlignment.Stretch };

			// A header with an icon (a page from the left drawer) has 4px more room below it.
			var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, icon != null ? 8 : 4) };
			var titleText = new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

			// As tall as the drawer buttons even without actions, so titles sit at the same height on every page.
			var firstRow = new DockPanel { MinHeight = DrawerButtonSize };
			if (titleAction != null)
			{
				titleAction.VerticalAlignment = VerticalAlignment.Top;
				DockPanel.SetDock(titleAction, Dock.Right);
				firstRow.Children.Add(titleAction);
			}

			// The page's icon (its left drawer item's) before the title.
			Control titled = titleText;
			titleText.Tag = TitleTag;
			if (icon != null)
			{
				var iconPath = AppIcons.Create(icon, 24, 2);
				iconPath.Margin = new Thickness(0, 0, 12, 0);
				titled = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { iconPath, titleText }, Tag = TitleTag };
				titleText.Tag = null;
			}

			header.Children.Add(firstRow);
			if (backLink != null)
			{
				backLink.VerticalAlignment = VerticalAlignment.Center;
				backLink.HorizontalAlignment = HorizontalAlignment.Left;
				firstRow.Children.Add(backLink);
				header.Children.Add(titled);
			}
			else
			{
				firstRow.Children.Add(titled);
			}

			if (!string.IsNullOrEmpty(subtitle))
			{
				header.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Tag = SubtitleTag });
			}

			DockPanel.SetDock(header, Dock.Top);
			column.Children.Add(header);
			column.Children.Add(content);
			KeepScrollBarsBeside(column);
			return column;
		}

		/// <summary>
		/// The room a song page's header (title and actions) keeps at the sides: the song page uses the
		/// whole width, under the drawer buttons over the top corners (40 wide, 4 from the edge), so its
		/// header keeps clear of them. The right side allows for the page's room for the scroll bar.
		/// </summary>
		private static Thickness SongHeaderMargin(double bottom) => new(40, 0, 40 - ScrollBarRoom, bottom);

		/// <summary>
		/// A song's page: the name with its details (key, tempo, beat) after it on the same line, and
		/// the action at the right. It sits a little higher than other pages, so the action lines up
		/// with the drawer buttons in the top corners (both 40 high, centred 24 from the top).
		/// </summary>
		/// <param name="key">The song's playing key, shown larger in the details (see <see cref="SetDetailsText"/>).</param>
		public static Control SongPage(string title, Control content, string? details, Control titleAction, string? key = null)
		{
			var titleText = new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 0) };
			var titleRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { titleText } };
			if (!string.IsNullOrEmpty(details))
			{
				// On the title's baseline (the smaller text sits a little less deep below it).
				var detailsText = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
				SetDetailsText(detailsText, details, key);
				titleRow.Children.Add(detailsText);
			}

			titleAction.VerticalAlignment = VerticalAlignment.Top;
			DockPanel.SetDock(titleAction, Dock.Right);
			var header = new DockPanel { Margin = SongHeaderMargin(4), Children = { titleAction, titleRow }, ZIndex = 1 };
			Grid.SetRow(content, 1);
			var page = new Grid
			{
				// The whole width (the lyrics and chord grid use it all), with room at the right for
				// the scroll bar, which sits beside the content (see KeepScrollBarsBeside).
				Margin = new Thickness(4, 4, 4 + ScrollBarRoom, 4),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				RowDefinitions = new RowDefinitions("Auto,*"),
				Children = { header, content },
			};
			KeepScrollBarsBeside(page);
			return page;
		}

		/// <summary>Changes the title and details of a <see cref="SongPage"/>, e.g. after the song is renamed.</summary>
		/// <param name="key">The song's playing key, shown larger in the details.</param>
		public static void SetSongTitle(Control songPage, string title, string? details, string? key = null)
		{
			if (songPage is not Grid { Children: [DockPanel { Children: [_, WrapPanel { Children: [TextBlock titleText, ..] } titleRow, ..] }, ..] })
			{
				return;
			}

			titleText.Text = title;
			var detailsText = titleRow.Children.Count > 1 ? titleRow.Children[1] as TextBlock : null;
			if (string.IsNullOrEmpty(details))
			{
				if (detailsText != null)
				{
					titleRow.Children.Remove(detailsText);
				}
			}
			else if (detailsText != null)
			{
				SetDetailsText(detailsText, details, key);
			}
			else
			{
				var added = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
				SetDetailsText(added, details, key);
				titleRow.Children.Add(added);
			}
		}

		/// <summary>
		/// Puts <paramref name="replacement"/> in the place of the title and details of a
		/// <see cref="SongPage"/> (e.g. the song's editable name while it is edited); null brings the
		/// title and details back.
		/// </summary>
		public static void SetSongTitleContent(Control songPage, Control? replacement)
		{
			if (songPage is not Grid { Children: [DockPanel header, ..] } || header.Children.Count < 2)
			{
				return;
			}

			if (header.Children.Count == 3 && header.Children[2] == replacement)
			{
				return;
			}

			while (header.Children.Count > 2)
			{
				header.Children.RemoveAt(2);
			}

			if (replacement != null)
			{
				// Laid out around the action at the right (the first child): its lines below it take the whole width.
				if (replacement is CornerWrapPanel around)
				{
					around.Corner = header.Children[0];
				}

				header.Children.Add(replacement);
			}

			header.Children[1].IsVisible = replacement == null;
		}

		/// <summary>
		/// Shows or hides the title and details of a <see cref="SongPage"/>. Hidden, the title action
		/// (e.g. the pencil) stays at the top right, over the content, which takes the whole page.
		/// </summary>
		public static void SetSongTitleShown(Control songPage, bool shown)
		{
			if (songPage is not Grid { Children: [DockPanel header, var content] })
			{
				return;
			}

			// A replacement (see SetSongTitleContent) is shown instead of the title and details.
			header.Children[1].IsVisible = shown && header.Children.Count == 2;
			for (var i = 2; i < header.Children.Count; i++)
			{
				header.Children[i].IsVisible = shown;
			}

			header.Margin = SongHeaderMargin(shown ? 4 : 0);
			// Hidden: the content starts at the top, under the action, so the header takes no room.
			header.HorizontalAlignment = shown ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
			header.VerticalAlignment = VerticalAlignment.Top;
			Grid.SetRow(content, shown ? 1 : 0);
			Grid.SetRowSpan(content, shown ? 1 : 2);
		}

		/// <summary>The room at a page's right for its scroll bars (the page's own margin there).</summary>
		public const double ScrollBarRoom = 16;

		/// <summary>
		/// A scroll bar is drawn over the right edge of what it scrolls. The page's scrolling areas
		/// reach out into the page's right margin, with what they scroll kept where it was: the scroll
		/// bar is beside the content (still lined up with the title's buttons), not over it.
		/// </summary>
		// The room around a page's column (see Page).
		private const double ColumnMargin = 4;

		/// <summary>
		/// Makes <paramref name="content"/> of a page use the app's whole width, as the lyrics page does:
		/// out past the page's margins (the room kept for the drawer buttons and around the column). E.g. a
		/// song previewed in the lyrics view, so it sits as it does when played.
		/// </summary>
		public static T WholeWidth<T>(T content) where T : Control
		{
			content.Margin = new Thickness(
				-(HomeView.PageMargin.Left + ColumnMargin),
				content.Margin.Top,
				-(HomeView.PageMargin.Right + ColumnMargin),
				content.Margin.Bottom);
			return content;
		}

		private static void KeepScrollBarsBeside(Control page)
		{
			page.Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType<ScrollViewer>(x))
			{
				Setters =
				{
					new Avalonia.Styling.Setter(Layoutable.MarginProperty, new Thickness(0, 0, -ScrollBarRoom, 0)),
					new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(0, 0, ScrollBarRoom, 0)),
				},
			});
		}

		internal const string AddIcon = "M12,5 V19 M5,12 H19";

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

		/// <summary>
		/// What a new password must have (the server's rules: ASP.NET Identity's own), as a small note
		/// under the box for it.
		/// </summary>
		public static TextBlock PasswordRules() => new()
		{
			Text = L.T("At least 6 characters, with an uppercase and a lowercase letter, a digit and a symbol (such as ! or #)."),
			FontSize = 12,
			Opacity = 0.7,
			TextWrapping = TextWrapping.Wrap,
			MaxWidth = 360,
			HorizontalAlignment = HorizontalAlignment.Left,
		};

		/// <summary>
		/// "Language" with a dropdown of the app's languages (English, Svenska), starting at
		/// <paramref name="code"/>: for a new account, whose language setting it becomes (the app's, and
		/// the emails' and texts' sent to them).
		/// </summary>
		/// <param name="chosen">Called with the code ("en" or "sv") whenever another is chosen.</param>
		public static Control LanguageChoice(string code, Action<string> chosen)
		{
			var languages = Localization.Language.Choices.Where(choice => choice.Code.Length > 0).ToList();
			var dropdown = new ComboBox
			{
				ItemsSource = languages.Select(choice => choice.Name).ToList(),
				SelectedIndex = Math.Max(0, languages.FindIndex(choice => choice.Code == code)),
				MinWidth = 140,
				VerticalAlignment = VerticalAlignment.Center,
			};
			dropdown.SelectionChanged += (_, _) =>
			{
				if (dropdown.SelectedIndex >= 0)
				{
					chosen(languages[dropdown.SelectedIndex].Code);
				}
			};
			DockPanel.SetDock(dropdown, Dock.Right);
			var label = new TextBlock { Text = L.T("Language"), VerticalAlignment = VerticalAlignment.Center };
			ToolTip.SetTip(label, L.T("The language of the app, and of the emails and texts Lyrious sends"));
			return new DockPanel { Children = { dropdown, label } };
		}

		/// <summary>A "+" button for a page title (e.g. to open a menu of ways to add something).</summary>
		public static Button AddButton(string description) => HomeView.IconButton(AddIcon, description);

		/// <summary>
		/// A hidden row with a name box, Add and Cancel; <c>Show</c> reveals it and focuses the box.
		/// Add (or Enter) passes the trimmed name to <paramref name="add"/>; Cancel hides it. Escape in the
		/// box leaves it (as in every text box, see <see cref="TextBoxFocus"/>); the next Escape is Cancel.
		/// Dock the row at the top of the page content.
		/// </summary>
		/// <param name="closeOnAdd">Hides the row (and empties the box) after Add, for a page that stays.</param>
		public static (Control Row, Action Show) NameRow(string placeholder, Action<string> add, bool closeOnAdd = false)
		{
			var nameBox = new TextBox { PlaceholderText = placeholder, VerticalContentAlignment = VerticalAlignment.Center };
			// The ✓ in the box, once a name is typed.
			var confirm = OkButton(L.T("Add"));
			OkInBox(nameBox, confirm, () => !string.IsNullOrWhiteSpace(nameBox.Text));
			// Escape (once the box is left) is Cancel, before the page's ✕.
			var cancel = EscapeKey.MarkAsClose(CancelButton(L.T("Cancel")), first: true);
			cancel.Margin = new Thickness(8, 0, 0, 0);
			DockPanel.SetDock(cancel, Dock.Right);
			var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16), IsVisible = false, Children = { cancel, nameBox } };
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
					if (closeOnAdd)
					{
						Close();
					}
				}
			}

			void Show()
			{
				row.IsVisible = true;
				nameBox.Focus();
			}

			nameBox.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Enter)
				{
					Submit();
					e.Handled = true;
				}
			};
			confirm.Click += (_, _) => Submit();
			cancel.Click += (_, _) => Close();
			return (row, Show);
		}

		// A check (OK) and a cross (cancel).
		private const string OkIcon = "M5,12 L10,17 L19,7";
		private const string CancelIcon = "M6,6 L18,18 M18,6 L6,18";

		/// <summary>An OK button: a check, highlighted (the accent colour), as every OK in the app is.</summary>
		public static Button OkButton(string description)
		{
			var button = HomeView.IconButton(OkIcon, description);
			button.Classes.Add("accent");
			// The check bolder than the other icons' lines.
			if (button.Content is Avalonia.Controls.Shapes.Shape check)
			{
				check.StrokeThickness = 3;
			}

			return button;
		}

		/// <summary>
		/// Puts an OK button (a ✓, see <see cref="OkButton"/>) inside the text box, at its right end:
		/// square and as high as the box. It shows only while <paramref name="changed"/> says the text
		/// was changed (so there's something to save). The box keeps its width either way: the text
		/// makes room for the button inside it.
		/// </summary>
		public static void OkInBox(TextBox box, Button ok, Func<bool> changed)
		{
			FitInBox(box, ok);
			box.InnerRightContent = ok;

			void Show() => ok.IsVisible = changed();
			box.TextChanged += (_, _) => Show();
			Show();
		}

		/// <summary>
		/// Shapes a button to sit inside <paramref name="box"/> (its inner right content): as high as the
		/// box inside its border.
		/// A button with only an icon is square too, as wide as it is high, with a smaller icon; one
		/// with text keeps its text's width.
		/// </summary>
		/// <param name="atEnd">
		/// Whether it's the last thing at the box's right end: then it's rounded like the box's right
		/// corners; else (another button follows it) it's square-cornered.
		/// </param>
		/// <param name="iconSize">The icon's size, for a button with only an icon.</param>
		public static void FitInBox(TextBox box, Button button, bool atEnd = true, double iconSize = 16)
		{
			button.Margin = new Thickness(0);
			button.MinWidth = 0;
			button.MinHeight = 0;
			button.VerticalAlignment = VerticalAlignment.Center;
			// Its height from the box's (inside its border), not from stretching: a box with its text
			// centred (VerticalContentAlignment) centres what's in it at its own height too.
			void Fit()
			{
				var height = box.Bounds.Height - box.BorderThickness.Top - box.BorderThickness.Bottom;
				// (Its height is unset, NaN, until the first time.)
				if (height > 0 && (double.IsNaN(button.Height) || Math.Abs(height - button.Height) > 0.5))
				{
					button.Height = height;
					if (button.Content is not string)
					{
						button.Width = height;
					}
				}
			}

			box.SizeChanged += (_, e) =>
			{
				if (e.HeightChanged)
				{
					Fit();
				}
			};
			Fit();
			button.HorizontalContentAlignment = HorizontalAlignment.Center;
			button.VerticalContentAlignment = VerticalAlignment.Center;
			// Rounded like the box's right corners, inside its border.
			var radius = Application.Current?.TryFindResource("ControlCornerRadius", out var found) == true && found is CornerRadius r ? r.TopRight : 4;
			var inner = atEnd ? Math.Max(0, radius - 1) : 0;
			button.CornerRadius = new CornerRadius(0, inner, inner, 0);
			if (button.Content is string)
			{
				// Text: room on its sides, as a text button has.
				button.Padding = new Thickness(10, 0);
				return;
			}

			button.Padding = new Thickness(0);
			if (button.Content is Avalonia.Controls.Shapes.Shape icon)
			{
				icon.Width = iconSize;
				icon.Height = iconSize;
				icon.Stretch = Stretch.Uniform;
			}
		}

		/// <summary>A cancel button: a cross, beside an <see cref="OkButton"/>.</summary>
		public static Button CancelButton(string description) => HomeView.IconButton(CancelIcon, description);

		/// <summary>
		/// The text of a drop-down that can be typed in (an editable <see cref="ComboBox"/>) in the middle of
		/// its height, as a text box's text (it sits higher otherwise), with a text box's room above and below
		/// it and <paramref name="leftPadding"/> before it. The text box inside may be narrower than a text
		/// box's usual least width, so in a narrow drop-down its text doesn't scroll out of sight to the left.
		/// </summary>
		public static void CenterTypedText(ComboBox box, double leftPadding)
		{
			var textPadding = Application.Current?.TryFindResource("TextControlThemePadding", out var padding) == true && padding is Thickness p
				? p
				: new Thickness(10, 6, 6, 5);
			box.Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType<TextBox>(x))
			{
				Setters =
				{
					new Avalonia.Styling.Setter(Layoutable.MinWidthProperty, 0.0),
					new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(leftPadding, textPadding.Top, 0, textPadding.Bottom)),
					new Avalonia.Styling.Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Stretch),
					new Avalonia.Styling.Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
				},
			});
		}

		/// <summary>
		/// The back link in a page header: a left chevron, then where it goes (e.g. "Search results"), in
		/// the text's colour (white in the dark theme) rather than a link's.
		/// </summary>
		private static HyperlinkButton HeaderLink(string text, Action click)
		{
			var content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				// Not underlined, as a link's text would be: it reads as the way back, not a web link.
				Children = { AppIcons.Create(BackIcon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextDecorations = [] } },
			};
			// On the content, so the link's own colour (also when pointed at) doesn't reach it.
			content.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, content.GetResourceObservable(LyriousTheme.ForegroundBrush));
			var link = new HyperlinkButton { Content = content, Padding = new Thickness(0) };
			link.Click += (_, _) => click();
			return link;
		}

		// A chevron pointing left: back.
		private const string BackIcon = "M15,6 L9,12 L15,18";

		// The previous and next buttons: arrows, so they aren't taken for the back link's chevron.
		private const string PreviousIcon = "M19,12 H5 M11,6 L5,12 L11,18";
		private const string NextIcon = "M5,12 H19 M13,6 L19,12 L13,18";

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

		// A check: "make this the current one".
		private const string OpenIcon = "M5,12 L10,17 L19,7";

		/// <summary>
		/// A list row's text with a check at the right when it's the current one (e.g. the band's
		/// current setlist). The check has the colour of the row's text.
		/// </summary>
		public static Control RowContent(string text, bool isCurrent)
		{
			var row = (DockPanel)RowContent(text, (string?)null);
			if (isCurrent)
			{
				row.Children.Insert(0, CurrentCheck(L.T("The current setlist")));
			}

			return row;
		}

		/// <summary>
		/// The quarter note (♩) that marks a list row's item as the current one, for docking at the
		/// row's right. It has the colour of the row's text, and is no taller than the text, so the
		/// row is as high as the others.
		/// </summary>
		public static Control CurrentCheck(string description)
		{
			var note = Note();
			note.Margin = new Thickness(12, -2, 0, -2);
			ToolTip.SetTip(note, description);
			DockPanel.SetDock(note, Dock.Right);
			return note;
		}

		/// <summary>
		/// The app's quarter note (♩), the sign of the current one (setlist, song, joined band), in the
		/// colour of the text around it, 24 high.
		/// </summary>
		public static Avalonia.Controls.Shapes.Path Note()
		{
			// A filled, tilted note head with its stem rising from the head's right side.
			var note = new Avalonia.Controls.Shapes.Path
			{
				Data = new GeometryGroup
				{
					Children =
					{
						new EllipseGeometry(new Rect(7, 14.6, 8.4, 5.8)) { Transform = new RotateTransform(-22, 11.2, 17.5) },
						new RectangleGeometry(new Rect(13.9, 3.5, 1.7, 13.6)),
					},
				},
				Width = 24,
				Height = 24,
				VerticalAlignment = VerticalAlignment.Center,
			};
			note.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, note.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			return note;
		}

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
			RedOnHover(button);

			var confirm = DangerButton(L.T("Delete"));
			var cancel = new Button { Content = L.T("Cancel") };
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

		/// <summary>
		/// A card's title (e.g. a setlist's or band's name); clicking it turns it into a box to change
		/// it. Enter, the ✓ in the box (shown once the name is changed) or leaving the box calls
		/// <paramref name="rename"/> with a new, non-empty name;
		/// Escape keeps the old one.
		/// </summary>
		public static Control EditableTitle(string text, Action<string> rename)
		{
			var host = new ContentControl();
			var title = new TextBlock
			{
				Text = text,
				FontSize = 20,
				FontWeight = FontWeight.SemiBold,
				TextWrapping = TextWrapping.Wrap,
				Cursor = new Cursor(StandardCursorType.Ibeam),
				Background = Brushes.Transparent,
				// As tall as the tool buttons (and the box), centred on their line, so nothing moves when editing.
				MinHeight = 40,
				Padding = new Thickness(0, 6),
				VerticalAlignment = VerticalAlignment.Center,
			};
			ToolTip.SetTip(title, L.T("Click to rename"));

			void Edit()
			{
				var box = new TextBox { Text = title.Text, FontSize = 20, FontWeight = FontWeight.SemiBold, MaxLength = 100, Height = 40, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
				var done = false;

				void Finish(bool save)
				{
					if (done)
					{
						return;
					}

					done = true;
					var newName = box.Text?.Trim() ?? "";
					host.Content = title;
					if (save && newName.Length > 0 && newName != title.Text)
					{
						title.Text = newName;
						rename(newName);
					}
				}

				// Escape leaves the box as in every text box (see TextBoxFocus): the name goes back to what it
				// was, so leaving it (LostFocus) renames nothing.
				box.KeyDown += (_, e) =>
				{
					if (e.Key == Key.Enter)
					{
						e.Handled = true;
						Finish(save: true);
					}
				};
				box.LostFocus += (_, _) => Finish(save: true);

				// A ✓ in the box once the name is changed; it saves, as Enter does. It doesn't take the
				// focus, so pressing it doesn't first leave the box.
				var ok = OkButton(L.T("Save the name"));
				ok.Focusable = false;
				OkInBox(box, ok, () => box.Text?.Trim() is { Length: > 0 } typed && typed != title.Text);
				ok.Click += (_, _) => Finish(save: true);
				box.AttachedToVisualTree += (_, _) => box.Focus();
				host.Content = box;
			}

			title.Tapped += (_, _) => Edit();
			host.Content = title;
			return host;
		}

		/// <summary>
		/// Gives an icon button (e.g. the trash) a red background with a white icon while the
		/// pointer is over it or it's pressed. Only the button itself: the buttons in a popup it opens
		/// (e.g. Cancel in "Delete?") hover grey as usual. (Through the button's resources they'd be
		/// red too, as a popup's content takes its owner's resources.)
		/// </summary>
		public static void RedOnHover(Button button)
		{
			button.Classes.Add(RedHoverClass);
			button.Styles.Add(RedHoverStyle(":pointerover", "#D13438"));
			button.Styles.Add(RedHoverStyle(":pressed", "#A4262C"));
			// The icon follows the button's text colour (white over the red), instead of the theme's.
			if (button.Content is Avalonia.Controls.Shapes.Shape icon)
			{
				icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, icon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			}
		}

		// Marks the buttons that hover red (see RedOnHover).
		private const string RedHoverClass = "red-hover";

		/// <summary>
		/// The red of a <see cref="RedOnHover"/> button in a state (":pointerover" or ":pressed"): on the
		/// part of its template that draws the background, as the theme's own hover does, and with a white icon.
		/// </summary>
		private static Avalonia.Styling.Style RedHoverStyle(string state, string color) => new(x =>
			Avalonia.Styling.Selectors.Name(
				Avalonia.Styling.Selectors.OfType<Avalonia.Controls.Presenters.ContentPresenter>(
					Avalonia.Styling.Selectors.Template(
						Avalonia.Styling.Selectors.Class(
							Avalonia.Styling.Selectors.Class(Avalonia.Styling.Selectors.OfType<Button>(x), RedHoverClass),
							state))),
				"PART_ContentPresenter"))
		{
			Setters =
			{
				new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse(color))),
				new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Brushes.White),
			},
		};

		/// <summary>A strong red button with bold white text, for confirming a delete or remove.</summary>
		public static Button DangerButton(string text)
		{
			var button = new Button
			{
				Content = text,
				FontWeight = FontWeight.Bold,
				Foreground = Brushes.White,
				Background = new SolidColorBrush(Color.Parse("#E81123")),
			};
			// Hovered and pressed: a lighter and a deeper red. Styles for this button only (not resources,
			// which the buttons in a popup it opens, e.g. Cancel, would take too).
			button.Classes.Add(RedHoverClass);
			button.Styles.Add(RedHoverStyle(":pointerover", "#FF2A2A"));
			button.Styles.Add(RedHoverStyle(":pressed", "#B0000D"));
			return button;
		}

		/// <summary>
		/// Gives a search box a small ✕ at its right end, shown while there's text: it empties the box
		/// (so everything is listed again) and leaves the focus in it.
		/// </summary>
		public static T WithClearButton<T>(T box) where T : TextBox
		{
			var cross = new Avalonia.Controls.Shapes.Path
			{
				Data = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				Width = 14,
				Height = 14,
				Stretch = Stretch.Uniform,
			};
			cross.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, cross.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			var clear = new Button
			{
				Content = cross,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Focusable = false,
				Cursor = new Cursor(StandardCursorType.Arrow),
				IsVisible = !string.IsNullOrEmpty(box.Text),
			};
			// The box's height, square.
			FitInBox(box, clear, iconSize: 14);
			ToolTip.SetTip(clear, L.T("Clear"));
			clear.Click += (_, _) =>
			{
				box.Text = string.Empty;
				box.Focus();
			};
			box.TextChanged += (_, _) => clear.IsVisible = !string.IsNullOrEmpty(box.Text);
			box.InnerRightContent = clear;
			return box;
		}

		/// <summary>A song's tags as small rounded labels flowing left to right.</summary>
		public static WrapPanel TagChips(System.Collections.Generic.IEnumerable<string> tags, double fontSize = 12)
		{
			var chips = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };
			foreach (var tag in tags)
			{
				chips.Children.Add(TagChip(tag, fontSize));
			}

			return chips;
		}

		/// <summary>
		/// One tag as a small rounded label. Its background is a tint of the text colour around it, so it
		/// stands out either way: lighter on a dark background (or a highlighted row with white text),
		/// darker on a light one.
		/// </summary>
		public static Border TagChip(string tag, double fontSize = 12)
		{
			var chip = new Border
			{
				CornerRadius = new CornerRadius(10),
				Padding = new Thickness(8, 1),
				VerticalAlignment = VerticalAlignment.Center,
				Child = new TextBlock { Text = tag, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center },
			};
			void Tint() => chip.Background = new SolidColorBrush(Avalonia.Controls.Documents.TextElement.GetForeground(chip) is ISolidColorBrush brush
				? Color.FromArgb(0x40, brush.Color.R, brush.Color.G, brush.Color.B)
				: Color.FromArgb(0x38, 0x80, 0x80, 0x80));
			Tint();
			// Again whenever the text colour around it changes (the theme, a row highlighted or hovered).
			chip.PropertyChanged += (_, e) =>
			{
				if (e.Property == Avalonia.Controls.Documents.TextElement.ForegroundProperty)
				{
					Tint();
				}
			};
			return chip;
		}

		/// <summary>
		/// A song's tempo, beat and keys for a list row (the order details go in everywhere), after
		/// <paramref name="note"/> (e.g. "in setlist") when there is one: the playing key in the full
		/// text colour, the rest (with the original key, when it differs) dimmed.
		/// </summary>
		/// <param name="transpose">The member's own transpose of the song: the key is shown moved by it, with the amount ("A (+2)").</param>
		/// <param name="transposeBesideKey">The amount right after the key, as large as it and without "capo" or brackets ("A +2"), e.g. in the right drawer.</param>
		/// <param name="centered">Each part in the middle of the line (e.g. beside a small caption), rather than on one baseline.</param>
		public static Control SongInfoText(SongDetails song, string? note = null, double fontSize = 14, int transpose = 0, bool transposeBesideKey = false, bool centered = false)
		{
			var key = MovedKey(song.PlayingKey, transpose);
			var original = transposeBesideKey ? OriginalKeyNote(song) : KeyNotes(song, transpose);
			var besideKey = transposeBesideKey ? TransposeValue(transpose) : "";
			var rest = SongInfo(song with { PlayingKey = "", OriginalKey = "" });
			var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			// On one baseline: the playing key is larger (see KeyScale), so the parts sit on their bottoms.
			// Lines only a little taller than the letters, so the larger key doesn't add room above and
			// below the row's line.
			void Add(string text, bool dim, double scale = 1)
			{
				if (text.Length > 0)
				{
					var size = Math.Round(fontSize * scale);
					line.Children.Add(new TextBlock { Text = text, FontSize = size, LineHeight = Math.Round(size * 1.15), Opacity = dim ? 0.7 : 1, VerticalAlignment = centered ? VerticalAlignment.Center : VerticalAlignment.Bottom });
				}
			}

			// "in setlist · 120 bpm · 4/4 · G (+2, orig. A)", with only the parts there are.
			var keyPart = key.Length > 0 || original.Length > 0;
			Add(string.IsNullOrEmpty(note) ? "" : note + (keyPart || rest.Length > 0 ? " · " : ""), true);
			Add(rest.Length == 0 ? "" : rest + (keyPart ? " · " : ""), true);
			Add(key, false, KeyScale);
			Add(besideKey.Length == 0 ? "" : (key.Length > 0 ? " " : "") + besideKey, true, KeyScale);
			Add(original.Length == 0 ? "" : (key.Length > 0 ? $" ({original})" : original), true);
			return line;
		}

		/// <summary>
		/// A song as the right drawer shows it (also the play toolbar's next song): its name in bold, and
		/// under it its tags at the left and its tempo, beat and key at the right (the key moved by the
		/// member's own transpose, with its value beside it, see <see cref="SongInfoLine"/>). When the room
		/// is too narrow for all of them, the tags wrap onto more lines.
		/// </summary>
		/// <param name="tagsOnOneLine">The tags on one line, those that don't fit cut off (e.g. in the play toolbar, which keeps its height); else they wrap.</param>
		public static StackPanel SongCard(SongDetails song, double nameSize, double detailsSize, double tagSize, bool tagsOnOneLine = false)
		{
			// The name and the line under it close together (the second line keeps its own room small too).
			var card = new StackPanel { Spacing = 0 };
			card.Children.Add(new TextBlock { Text = song.Name, FontSize = nameSize, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
			var hasDetails = SongInfo(song).Length > 0;
			var tags = SongTags.Sorted(song.Tags);
			if (hasDetails || tags.Count > 0)
			{
				var details = new SongInfoLine(song, fontSize: detailsSize);
				DockPanel.SetDock(details, Dock.Right);
				var wrapping = TagChips(tags, tagSize);
				Control chips = wrapping;
				if (tagsOnOneLine)
				{
					// In a row that doesn't wrap, clipped where the room ends.
					var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = wrapping.ItemSpacing };
					var each = System.Linq.Enumerable.ToList(wrapping.Children);
					wrapping.Children.Clear();
					row.Children.AddRange(each);
					chips = new Border { ClipToBounds = true, Child = row };
				}

				chips.Margin = new Thickness(0, 0, hasDetails && tags.Count > 0 ? 8 : 0, 0);
				chips.VerticalAlignment = VerticalAlignment.Center;
				chips.HorizontalAlignment = HorizontalAlignment.Left;
				card.Children.Add(new DockPanel { Children = { details, chips } });
			}

			return card;
		}

		/// <summary>How much larger the playing key is than the tempo and beat beside it (half as large again), so it stands out.</summary>
		public const double KeyScale = 1.5;

		/// <summary>
		/// Writes a song's details line (e.g. "120 bpm · 4/4 · G (orig. A)") into the text block with the
		/// playing key in it half as large again (see <see cref="KeyScale"/>): where it starts the line or
		/// follows a " · ". Just the text when there's no key, or it isn't in the line.
		/// </summary>
		/// <param name="key">The playing key as it's shown in the text (moved by the member's transpose, see <see cref="MovedKey"/>).</param>
		public static void SetDetailsText(TextBlock block, string? text, string? key)
		{
			block.Inlines?.Clear();
			text ??= "";
			var playing = key?.Trim() ?? "";
			var at = playing.Length == 0 ? -1
				: text.StartsWith(playing, StringComparison.Ordinal) ? 0
				: text.IndexOf(" · " + playing, StringComparison.Ordinal) is >= 0 and var found ? found + 3
				: -1;
			if (at < 0)
			{
				block.Text = text;
				return;
			}

			block.Text = null;
			block.Inlines ??= [];
			block.Inlines.Add(new Avalonia.Controls.Documents.Run(text[..at]));
			block.Inlines.Add(new Avalonia.Controls.Documents.Run(playing) { FontSize = Math.Round(block.FontSize * KeyScale) });
			block.Inlines.Add(new Avalonia.Controls.Documents.Run(text[(at + playing.Length)..]));
		}

		/// <summary>"orig. A" when the song's original key is set and isn't the playing key; else empty.</summary>
		public static string OriginalKeyNote(SongDetails song)
		{
			var original = song.OriginalKey?.Trim() ?? "";
			return original.Length > 0 && !original.Equals(song.PlayingKey?.Trim(), StringComparison.Ordinal) ? $"orig. {original}" : "";
		}

		/// <summary>
		/// The member's own transpose as they count it: semitones ("+2", "−1"), or with a capo
		/// ("capo +2", see <see cref="DisplaySettings.CapoMode"/>); empty for none.
		/// </summary>
		public static string TransposeAmount(int transpose)
		{
			if (transpose == 0)
			{
				return "";
			}

			return DisplaySettings.CapoMode ? L.T("capo {0}", TransposeValue(transpose)) : TransposeValue(transpose);
		}

		/// <summary>The member's own transpose as just a number as they count it ("+2", "−1"); empty for none.</summary>
		public static string TransposeValue(int transpose)
			// A capo counts the other way: chords moved down 2 are played with the capo 2 frets up.
			=> transpose == 0 ? "" : (DisplaySettings.CapoMode ? -transpose : transpose).ToString("+0;−0");

		/// <summary>The playing key moved by the member's transpose ("G" by +2 is "A"); the key itself when either isn't set.</summary>
		public static string MovedKey(string? key, int transpose)
		{
			var playing = key?.Trim() ?? "";
			return transpose == 0 || playing.Length == 0 ? playing : Transpose.Chord(playing, transpose, playing) ?? playing;
		}

		// What goes in brackets after the playing key: the member's transpose and the original key, "+2, orig. A".
		private static string KeyNotes(SongDetails song, int transpose)
			=> string.Join(", ", System.Linq.Enumerable.Where(new[] { TransposeAmount(transpose), OriginalKeyNote(song) }, part => part.Length > 0));

		/// <summary>A song's tempo, beat and keys (the order details go in everywhere), e.g. "120 bpm · 4/4 · G# (orig. A)"; empty if none are set.</summary>
		/// <param name="transpose">The member's own transpose of the song: the key is shown moved by it, with the amount ("A (+2, orig. A)").</param>
		public static string SongInfo(SongDetails song, int transpose = 0)
		{
			var parts = new System.Collections.Generic.List<string>();
			var key = MovedKey(song.PlayingKey, transpose);
			var original = KeyNotes(song, transpose);
			var keys = key.Length > 0 || original.Length > 0
				? key.Length == 0 ? original : original.Length == 0 ? key : $"{key} ({original})"
				: "";
			if (song.Tempo > 0)
			{
				parts.Add($"{song.Tempo.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} bpm");
			}

			if (!string.IsNullOrWhiteSpace(song.Beat))
			{
				parts.Add(song.Beat);
			}

			if (keys.Length > 0)
			{
				parts.Add(keys);
			}

			return string.Join(" · ", parts);
		}

		public static Control List(Control[] rows, string emptyText)
		{
			if (rows.Length == 0)
			{
				return new TextBlock { Text = emptyText, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
			}

			// Up and down go through the rows (see ListKeys).
			var list = ListKeys.Stepped(new StackPanel { Spacing = 8 });
			list.Children.AddRange(rows);
			return new ScrollViewer { Content = list };
		}
	}
}
