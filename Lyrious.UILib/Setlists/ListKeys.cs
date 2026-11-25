using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A list the keyboard steps through: up and down go to the item before or after (selecting it, or
	/// focusing it, as the list does), and the space bar checks or unchecks it in a list with check boxes.
	/// The page's lists take the keys (not the right drawer's); see <see cref="Find"/>.
	/// </summary>
	internal interface IListKeys
	{
		/// <summary>Goes to the item before (-1) or after (1); from none, the first or the last.</summary>
		void Step(int direction);

		/// <summary>Checks or unchecks the item gone to; false when the list has no check boxes (or no item is gone to).</summary>
		bool ToggleCheck();
	}

	/// <summary>The lists on a page that the keyboard steps through (see <see cref="IListKeys"/>), and the items in them.</summary>
	internal sealed class ListKeys
	{
		private ListKeys()
		{
		}

		/// <summary>What steps through the list this is set on.</summary>
		public static readonly AttachedProperty<IListKeys?> StepperProperty =
			AvaloniaProperty.RegisterAttached<ListKeys, Control, IListKeys?>("Stepper");

		/// <summary>Marks a list's item, where the list has other things among its items too (e.g. buttons above them).</summary>
		public static readonly AttachedProperty<bool> IsItemProperty =
			AvaloniaProperty.RegisterAttached<ListKeys, Control, bool>("IsItem");

		public static void SetStepper(Control list, IListKeys? stepper) => list.SetValue(StepperProperty, stepper);

		public static IListKeys? GetStepper(Control list) => list.GetValue(StepperProperty);

		public static void SetIsItem(Control item, bool value) => item.SetValue(IsItemProperty, value);

		public static bool GetIsItem(Control item) => item.GetValue(IsItemProperty);

		/// <summary>Makes the panel's items (those marked, else all of them) a list the keyboard steps through.</summary>
		public static T Stepped<T>(T list) where T : Panel
		{
			SetStepper(list, new PanelListKeys(list));
			return list;
		}

		/// <summary>
		/// The list on the page the keys go to: the one with the focus in it, else the first shown. Null
		/// when the page has none.
		/// </summary>
		public static IListKeys? Find(Control page, IInputElement? focused)
		{
			var lists = new[] { page }.Concat(page.GetVisualDescendants().OfType<Control>())
				.Where(control => GetStepper(control) != null && control.IsEffectivelyVisible)
				.ToList();
			var withFocus = focused is Visual visual ? lists.FirstOrDefault(list => list.IsVisualAncestorOf(visual)) : null;
			return (withFocus ?? lists.FirstOrDefault()) is { } found ? GetStepper(found) : null;
		}

		/// <summary>The item's check box, if it has one (not one of a toolbar's).</summary>
		public static CheckBox? CheckBoxOf(Control item)
			=> item.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(box => box.IsEffectivelyVisible && box.IsEffectivelyEnabled && !box.IsThreeState);

		/// <summary>Checks or unchecks the box; false when there's none.</summary>
		public static bool Toggle(CheckBox? box)
		{
			if (box == null)
			{
				return false;
			}

			box.IsChecked = box.IsChecked != true;
			return true;
		}

		/// <summary>
		/// Shows the item as the one gone to with the keyboard: outlined inside its edge in the selection
		/// colour, without changing its size (or not, <paramref name="on"/> false).
		/// </summary>
		public static void Outline(Border item, bool on)
		{
			if (on && item.TryFindResource(LyriousTheme.SelectionBrush, item.ActualThemeVariant, out var brush) && brush is ISolidColorBrush solid)
			{
				item.BoxShadow = new BoxShadows(new BoxShadow { Color = solid.Color, Spread = 2, IsInset = true });
			}
			else
			{
				item.BoxShadow = default;
			}
		}
	}

	/// <summary>
	/// Steps through a panel's items: a button is focused, any other item (e.g. a card) outlined. The
	/// item gone to is the one with the focus in it, else the one gone to last.
	/// </summary>
	internal sealed class PanelListKeys(Panel list) : IListKeys
	{
		private Control? current;

		private List<Control> Items()
		{
			var all = list.Children.Where(child => child.IsVisible).ToList();
			var marked = all.Where(ListKeys.GetIsItem).ToList();
			return marked.Count > 0 ? marked : all;
		}

		private Control? Current(List<Control> items)
		{
			var focused = TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement() as Visual;
			return items.FirstOrDefault(item => focused != null && (item == focused || item.IsVisualAncestorOf(focused)))
			       ?? (current != null && items.Contains(current) ? current : null);
		}

		public void Step(int direction)
		{
			var items = Items();
			if (items.Count == 0)
			{
				return;
			}

			var at = Current(items) is { } item ? items.IndexOf(item) : -1;
			var next = items[at < 0 ? (direction > 0 ? 0 : items.Count - 1) : Math.Clamp(at + direction, 0, items.Count - 1)];
			if (current is Border before && before != next)
			{
				ListKeys.Outline(before, false);
			}

			current = next;
			if (next is Button button)
			{
				button.Focus(NavigationMethod.Directional);
			}
			else if (next is Border card)
			{
				ListKeys.Outline(card, true);
			}

			next.BringIntoView();
		}

		public bool ToggleCheck()
			=> Current(Items()) is { } item && ListKeys.Toggle(ListKeys.CheckBoxOf(item));
	}
}
