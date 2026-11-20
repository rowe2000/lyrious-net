using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lyrious.UILib
{
	/// <summary>
	/// Escape presses the close button (✕) on screen, if there is one. Mark a close button with
	/// <see cref="MarkAsClose"/>. Escape that something else already handled (a text box's own use,
	/// a flyout closing, ending focus mode) is left alone. While a text box is being typed in, Escape
	/// first only leaves it (see <see cref="TextBoxFocus"/>); the next one closes.
	///
	/// With several on screen: an open drawer that slides over the page first, then the page's own ✕,
	/// then a pinned drawer. Within each, one marked to go first (e.g. Cancel while editing, before
	/// the page's ✕) wins. Close buttons in a closed drawer, hidden or disabled ones don't count.
	/// </summary>
	public static class EscapeKey
	{
		private const string CloseClass = "escape-closes";
		private const string FirstClass = "escape-closes-first";
		private const string BeforeClass = "escape-closes-before-all";

		/// <summary>Makes Escape press the button (when it's the close button that counts, see above).</summary>
		/// <param name="first">Goes before other close buttons in the same place, e.g. Cancel while editing before the page's ✕.</param>
		/// <param name="before">Goes before every other close button, wherever it is, e.g. ending a search in a pinned drawer before the page's ✕.</param>
		public static T MarkAsClose<T>(T button, bool first = false, bool before = false) where T : Button
		{
			button.Classes.Add(CloseClass);
			if (before)
			{
				button.Classes.Add(BeforeClass);
			}

			if (first)
			{
				button.Classes.Add(FirstClass);
			}

			return button;
		}

		/// <summary>Called once at startup: listens on every window (and the browser's root) for Escape.</summary>
		public static void Register()
		{
			InputElement.KeyDownEvent.AddClassHandler<TopLevel>((top, e) =>
			{
				if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || e.Handled)
				{
					return;
				}

				// Typing in a text box: this Escape only leaves it; the next one closes.
				if (TextBoxFocus.Leave(top))
				{
					e.Handled = true;
					return;
				}

				if (FindClose(top) is { } close)
				{
					e.Handled = true;
					close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
				}
			});
		}

		private static Button? FindClose(TopLevel top)
		{
			Button? best = null;
			var bestRank = int.MaxValue;
			foreach (var button in top.GetVisualDescendants().OfType<Button>())
			{
				if (!button.Classes.Contains(CloseClass) || !button.IsEffectivelyVisible || !button.IsEffectivelyEnabled
				    || Rank(button) is not { } place)
				{
					continue;
				}

				// One that goes before all others (e.g. ending a search) wins wherever it is.
			var rank = button.Classes.Contains(BeforeClass) ? -1 : place * 2 + (button.Classes.Contains(FirstClass) ? 0 : 1);

				// Equal rank: the later one in the tree, the one drawn on top.
				if (rank <= bestRank)
				{
					best = button;
					bestRank = rank;
				}
			}

			return best;
		}

		/// <summary>0 in an open overlay drawer, 1 on the page, 2 in a pinned drawer; null in a closed drawer.</summary>
		private static int? Rank(Button button)
		{
			foreach (var split in button.GetVisualAncestors().OfType<SplitView>())
			{
				if (split.Pane is Visual pane && (ReferenceEquals(pane, button) || pane.IsVisualAncestorOf(button)))
				{
					if (!split.IsPaneOpen)
					{
						return null;
					}

					return split.DisplayMode is SplitViewDisplayMode.Inline or SplitViewDisplayMode.CompactInline ? 2 : 0;
				}
			}

			return 1;
		}
	}
}
