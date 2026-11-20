using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
		public static Control Page(string title, Control content, string? backText = null, Action? back = null, string? subtitle = null, Control? titleAction = null, bool closable = false, string? icon = null)
			=> Page(title, content, backText != null && back != null ? BackLink(backText, back) : null, subtitle, WithClose(titleAction, closable), icon);

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
		private static Control Page(string title, Control content, Control? backLink, string? subtitle = null, Control? titleAction = null, string? icon = null)
		{
			// The whole width of the app (as the song pages), not a narrow column in the middle.
			var column = new DockPanel { Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Stretch };

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
			if (icon != null)
			{
				var iconPath = AppIcons.Create(icon, 24, 2);
				iconPath.Margin = new Thickness(0, 0, 12, 0);
				titled = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { iconPath, titleText } };
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
				header.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
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
		public static Control SongPage(string title, Control content, string? details, Control titleAction)
		{
			var titleText = new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 0) };
			var titleRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { titleText } };
			if (!string.IsNullOrEmpty(details))
			{
				// On the title's baseline (the smaller text sits a little less deep below it).
				titleRow.Children.Add(new TextBlock { Text = details, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) });
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
		public static void SetSongTitle(Control songPage, string title, string? details)
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
				detailsText.Text = details;
			}
			else
			{
				titleRow.Children.Add(new TextBlock { Text = details, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) });
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
		/// Add (or Enter) passes the trimmed name to <paramref name="add"/>; Cancel hides it. Escape in the
		/// box leaves it (as in every text box, see <see cref="TextBoxFocus"/>); the next Escape is Cancel.
		/// Dock the row at the top of the page content.
		/// </summary>
		/// <param name="closeOnAdd">Hides the row (and empties the box) after Add, for a page that stays.</param>
		public static (Control Row, Action Show) NameRow(string placeholder, Action<string> add, bool closeOnAdd = false)
		{
			var nameBox = new TextBox { PlaceholderText = placeholder, VerticalContentAlignment = VerticalAlignment.Center };
			var confirm = OkButton(L.T("Add"));
			confirm.IsEnabled = false;
			// Escape (once the box is left) is Cancel, before the page's ✕.
			var cancel = EscapeKey.MarkAsClose(CancelButton(L.T("Cancel")), first: true);

			var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 0, 0), Children = { confirm, cancel } };
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

			nameBox.TextChanged += (_, _) => confirm.IsEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
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
				Margin = new Thickness(12, -2, 0, -2),
				VerticalAlignment = VerticalAlignment.Center,
			};
			note.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, note.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			ToolTip.SetTip(note, description);
			DockPanel.SetDock(note, Dock.Right);
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
		/// it. Enter (or leaving the box) calls <paramref name="rename"/> with a new, non-empty name;
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
				box.AttachedToVisualTree += (_, _) =>
				{
					box.Focus();
					box.SelectAll();
				};
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
				Padding = new Thickness(6),
				Margin = new Thickness(0, 0, 2, 0),
				MinHeight = 0,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				VerticalAlignment = VerticalAlignment.Center,
				Focusable = false,
				Cursor = new Cursor(StandardCursorType.Arrow),
				IsVisible = !string.IsNullOrEmpty(box.Text),
			};
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
		/// A song's keys, tempo and beat for a list row, after <paramref name="note"/> (e.g. "in setlist")
		/// when there is one: the playing key in the full text colour, the rest (with the original key,
		/// when it differs) dimmed.
		/// </summary>
		public static Control SongInfoText(SongDetails song, string? note = null, double fontSize = 14)
		{
			var key = song.PlayingKey?.Trim() ?? "";
			var original = OriginalKeyNote(song);
			var rest = SongInfo(song with { PlayingKey = "", OriginalKey = "" });
			var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			void Add(string text, bool dim)
			{
				if (text.Length > 0)
				{
					line.Children.Add(new TextBlock { Text = text, FontSize = fontSize, Opacity = dim ? 0.7 : 1 });
				}
			}

			// "in setlist · G (orig. A) · 120 bpm · 4/4", with only the parts there are.
			var keyPart = key.Length > 0 || original.Length > 0;
			Add(string.IsNullOrEmpty(note) ? "" : note + (keyPart || rest.Length > 0 ? " · " : ""), true);
			Add(key, false);
			Add(original.Length == 0 ? "" : (key.Length > 0 ? $" ({original})" : original), true);
			Add(rest.Length == 0 ? "" : (keyPart ? " · " : "") + rest, true);
			return line;
		}

		/// <summary>"orig. A" when the song's original key is set and isn't the playing key; else empty.</summary>
		public static string OriginalKeyNote(SongDetails song)
		{
			var original = song.OriginalKey?.Trim() ?? "";
			return original.Length > 0 && !original.Equals(song.PlayingKey?.Trim(), StringComparison.Ordinal) ? $"orig. {original}" : "";
		}

		/// <summary>A song's keys, tempo and beat, e.g. "G# (orig. A) · 120 bpm · 4/4"; empty if none are set.</summary>
		public static string SongInfo(SongDetails song)
		{
			var parts = new System.Collections.Generic.List<string>();
			var key = song.PlayingKey?.Trim() ?? "";
			var original = OriginalKeyNote(song);
			if (key.Length > 0 || original.Length > 0)
			{
				parts.Add(key.Length == 0 ? original : original.Length == 0 ? key : $"{key} ({original})");
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
