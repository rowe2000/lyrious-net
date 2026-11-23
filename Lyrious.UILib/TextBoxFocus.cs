using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lyrious.UILib
{
	/// <summary>
	/// How a text box gives up the focus, the same everywhere in the app:
	/// <list type="bullet">
	/// <item>A click (or tap) outside the text box that's being typed in takes the focus from it, as in
	/// most apps; a click in another text box goes to that one. Buttons, drop-downs and the like take
	/// the focus themselves, as before.</item>
	/// <item>Escape in the text box being typed in undoes what was typed there (its text goes back to
	/// what it was when it got the focus) and leaves it; the next Escape does what Escape
	/// does, e.g. cancels or closes the view (see <see cref="EscapeKey"/>). A text box's own use of
	/// Escape (e.g. cancelling a rename) comes first.</item>
	/// <item>A drop-down likewise: Escape while the focus is in it (its text, for one that can be typed
	/// in, or the drop-down itself) and its list is closed puts back the value it had when the focus
	/// came to it, also undoing items picked since, and leaves it. With the list open, Escape closes
	/// the list, as before.</item>
	/// <item>When the view with the text box closes while it's being typed in, the text box loses the
	/// focus first (its LostFocus is raised), so what was typed is handled as when leaving it by hand,
	/// e.g. taken or saved.</item>
	/// <item>When the room to type in changes (the text box gets narrower or wider, or a button
	/// inside it, e.g. a ✓ or ✕, shows or hides) while it's being typed in, the caret is scrolled
	/// back into sight, so it's never hidden under those buttons.</item>
	/// </list>
	/// Views that act on LostFocus (take the value, close a box) need nothing more.
	/// </summary>
	public static class TextBoxFocus
	{
		// The text box being typed in, until it loses the focus, and its text when it got it.
		private static TextBox? focused;
		private static string? textWhenFocused;

		// The drop-down the focus is in (its list may be open), until the focus leaves it, and its value
		// when the focus came: its text for one that can be typed in, else its selected item.
		private static ComboBox? combo;
		private static object? comboValue;

		/// <summary>Called once at startup: on every window (and the browser's root), for every text box.</summary>
		public static void Register()
		{
			InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
			InputElement.KeyDownEvent.AddClassHandler<Control>(OnKeyDown);
			InputElement.GotFocusEvent.AddClassHandler<TextBox>(OnGotFocus);
			InputElement.LostFocusEvent.AddClassHandler<TextBox>(OnLostFocus);
			InputElement.IsKeyboardFocusWithinProperty.Changed.AddClassHandler<ComboBox>((box, _) => OnComboFocusChanged(box));
			ComboBox.IsDropDownOpenProperty.Changed.AddClassHandler<ComboBox>((box, _) => OnComboFocusChanged(box));
			// The text box's own scroller: its size is the room to type in.
			Visual.BoundsProperty.Changed.AddClassHandler<ScrollViewer>((scroller, e) => OnTypingRoomChanged(scroller, e));
		}

		/// <summary>The room to type in a text box changed: the caret back in sight, once the text box is laid out anew.</summary>
		private static void OnTypingRoomChanged(ScrollViewer scroller, AvaloniaPropertyChangedEventArgs e)
		{
			if (scroller.TemplatedParent is not TextBox box || !box.IsKeyboardFocusWithin
			    || e.OldValue is not Rect before || e.NewValue is not Rect after || Math.Abs(before.Width - after.Width) < 0.5)
			{
				return;
			}

			Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowCaret(box), Avalonia.Threading.DispatcherPriority.Background);
		}

		/// <summary>
		/// Scrolls the text box so its caret shows: the caret is moved off and back (which scrolls to it),
		/// and the selection is put back as it was.
		/// </summary>
		private static void ShowCaret(TextBox box)
		{
			var (start, end, caret) = (box.SelectionStart, box.SelectionEnd, box.CaretIndex);
			var length = box.Text?.Length ?? 0;
			box.CaretIndex = caret > 0 ? caret - 1 : Math.Min(1, length);
			box.CaretIndex = caret;
			if (start != end)
			{
				box.SelectionStart = start;
				box.SelectionEnd = end;
			}
		}

		/// <summary>The focus came to a drop-down (its value is kept, for Escape), or left it (its list closed too).</summary>
		private static void OnComboFocusChanged(ComboBox box)
		{
			if (box.IsKeyboardFocusWithin || box.IsDropDownOpen)
			{
				if (!ReferenceEquals(combo, box))
				{
					combo = box;
					comboValue = box.IsEditable ? box.Text : box.SelectedItem;
				}

				return;
			}

			// Checked a moment later: the focus passes between the drop-down, its text and its list.
			Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				if (ReferenceEquals(combo, box) && !box.IsKeyboardFocusWithin && !box.IsDropDownOpen)
				{
					combo = null;
					comboValue = null;
				}
			});
		}

		/// <summary>Whether the focus is in the drop-down <see cref="combo"/> (in it or its text), with its list closed.</summary>
		private static bool InClosedCombo(Visual? visual)
			=> combo is { IsDropDownOpen: false } box && visual != null && (ReferenceEquals(visual, box) || box.IsVisualAncestorOf(visual));

		/// <summary>A press outside the text box being typed in: the focus leaves it.</summary>
		private static void OnPressed(TopLevel top, PointerPressedEventArgs e)
		{
			// Pop-ups (a text box's own menu for copy and paste, a drop-down's list) act on the text box
			// or take the focus themselves.
			if (top is PopupRoot || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) != null)
			{
				return;
			}

			if (top.FocusManager?.GetFocusedElement() is not TextBox)
			{
				return;
			}

			// There's no "no focus": it goes to what was pressed, if that takes the focus, else to the
			// page's root.
			if (FocusableAncestor(e.Source as Visual) is { } target)
			{
				target.Focus(NavigationMethod.Pointer);
			}
			else
			{
				FocusRoot(top, NavigationMethod.Pointer);
			}
		}

		/// <summary>
		/// Escape in the text box being typed in, or a drop-down with the focus (see <see cref="EscapeKey"/>):
		/// its text (or selected item) goes back to what it was when the focus came to it, and the focus
		/// goes to the page's root. True if it did, so the key does nothing more this time.
		/// </summary>
		public static bool Leave(TopLevel top)
		{
			var focusedElement = top.FocusManager?.GetFocusedElement() as Visual;
			if (InClosedCombo(focusedElement) && combo is { } box1)
			{
				// Undone first, so whatever acts on leaving it sees the value as it was.
				if (box1.IsEditable)
				{
					if (box1.Text != comboValue as string)
					{
						box1.Text = comboValue as string;
					}
				}
				else if (!Equals(box1.SelectedItem, comboValue))
				{
					box1.SelectedItem = comboValue;
				}

				return FocusRoot(top, NavigationMethod.Unspecified);
			}

			if (focusedElement is not TextBox box)
			{
				return false;
			}

			// Undone first, so whatever acts on leaving it (LostFocus) sees the text as it was.
			if (ReferenceEquals(box, focused) && box.Text != textWhenFocused)
			{
				box.Text = textWhenFocused;
			}

			return FocusRoot(top, NavigationMethod.Unspecified);
		}

		/// <summary>The focus to the page's root, which takes it without being a Tab stop.</summary>
		private static bool FocusRoot(TopLevel top, NavigationMethod method)
		{
			if (top.Content is not InputElement root)
			{
				return false;
			}

			root.Focusable = true;
			root.IsTabStop = false;
			return root.Focus(method);
		}

		/// <summary>
		/// Escape on its way up from the text box being typed in, at the first element above it: after
		/// the text box's own handlers (e.g. cancelling a rename), before anything above that uses
		/// Escape (a drawer closing, a close button, see <see cref="EscapeKey"/>). It only leaves the text box.
		/// </summary>
		private static void OnKeyDown(Control element, KeyEventArgs e)
		{
			if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || e.Handled || element is TextBox
			    || !(InClosedCombo(e.Source as Visual) || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is { IsFocused: true })
			    || TopLevel.GetTopLevel(element) is not { } top)
			{
				return;
			}

			if (Leave(top))
			{
				e.Handled = true;
			}
		}

		/// <summary>The nearest element at or above <paramref name="visual"/> that takes the focus (not a text box).</summary>
		private static InputElement? FocusableAncestor(Visual? visual)
		{
			for (var current = visual; current != null && current is not TopLevel; current = current.GetVisualParent())
			{
				if (current is InputElement { Focusable: true, IsEffectivelyEnabled: true } element and not TextBox)
				{
					return element;
				}
			}

			return null;
		}

		private static void OnGotFocus(TextBox box, FocusChangedEventArgs e)
		{
			if (!ReferenceEquals(e.Source, box) || ReferenceEquals(focused, box))
			{
				return;
			}

			Forget();
			focused = box;
			textWhenFocused = box.Text;
			box.DetachedFromVisualTree += OnDetached;
		}

		private static void OnLostFocus(TextBox box, FocusChangedEventArgs e)
		{
			if (ReferenceEquals(e.Source, box) && ReferenceEquals(focused, box))
			{
				Forget();
			}
		}

		/// <summary>
		/// The text box being typed in left the screen (its view closed): it loses the focus as if left by
		/// hand, so its LostFocus handlers take what was typed.
		/// </summary>
		private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
		{
			if (sender is not TextBox box || !ReferenceEquals(focused, box))
			{
				return;
			}

			Forget();
			box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent) { Source = box });
		}

		private static void Forget()
		{
			if (focused != null)
			{
				focused.DetachedFromVisualTree -= OnDetached;
				focused = null;
				textWhenFocused = null;
			}
		}
	}
}
