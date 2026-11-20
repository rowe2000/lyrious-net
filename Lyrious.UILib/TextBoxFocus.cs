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
	/// <item>When the view with the text box closes while it's being typed in, the text box loses the
	/// focus first (its LostFocus is raised), so what was typed is handled as when leaving it by hand,
	/// e.g. taken or saved.</item>
	/// </list>
	/// Views that act on LostFocus (take the value, close a box) need nothing more.
	/// </summary>
	public static class TextBoxFocus
	{
		// The text box being typed in, until it loses the focus.
		private static TextBox? focused;

		/// <summary>Called once at startup: on every window (and the browser's root), for every text box.</summary>
		public static void Register()
		{
			InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
			InputElement.GotFocusEvent.AddClassHandler<TextBox>(OnGotFocus);
			InputElement.LostFocusEvent.AddClassHandler<TextBox>(OnLostFocus);
		}

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
			// page's root, which takes it without being a Tab stop.
			if (FocusableAncestor(e.Source as Visual) is { } target)
			{
				target.Focus(NavigationMethod.Pointer);
			}
			else if (top.Content is InputElement root)
			{
				root.Focusable = true;
				root.IsTabStop = false;
				root.Focus(NavigationMethod.Pointer);
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
			}
		}
	}
}
