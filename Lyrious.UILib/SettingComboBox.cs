using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace Lyrious.UILib
{
	/// <summary>
	/// A dropdown for a setting: the value chosen counts (and is saved) as soon as it's chosen. Escape
	/// never puts back the one before (as a plain dropdown does): with the list open it only closes the
	/// list, keeping what's chosen; with it closed it goes on to what Escape does there (e.g. closing
	/// the settings page), the setting left as it is.
	/// </summary>
	public class SettingComboBox : ComboBox
	{
		// Looks and behaves as a plain dropdown (its theme), but for Escape.
		protected override Type StyleKeyOverride => typeof(ComboBox);

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
			{
				if (IsDropDownOpen)
				{
					IsDropDownOpen = false;
					e.Handled = true;
				}

				return;
			}

			base.OnKeyDown(e);
		}
	}
}
