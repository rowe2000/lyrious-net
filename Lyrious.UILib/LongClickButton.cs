using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Lyrious.UILib
{
	/// <summary>
	/// A button that also has a longclick: pressed and held for <see cref="LongClickToggleButton.Duration"/>
	/// (mouse or touch), or clicked with the right mouse button. A longclick raises
	/// <see cref="LongClick"/> instead of the ordinary click.
	/// </summary>
	public sealed class LongClickButton : Button
	{
		private readonly DispatcherTimer timer = new() { Interval = LongClickToggleButton.Duration };

		// The press became a longclick: the click when it's let go is left out.
		private bool longClicked;

		/// <summary>Raised when the button has been held long enough (while still held), or right-clicked.</summary>
		public event Action? LongClick;

		// Styled as an ordinary button.
		protected override Type StyleKeyOverride => typeof(Button);

		public LongClickButton()
		{
			timer.Tick += (_, _) =>
			{
				timer.Stop();
				longClicked = true;
				LongClick?.Invoke();
			};

			// Tunnelling, so the press is seen before the button handles it.
			AddHandler(PointerPressedEvent, (_, e) =>
			{
				var properties = e.GetCurrentPoint(this).Properties;
				if (properties.IsLeftButtonPressed)
				{
					longClicked = false;
					timer.Start();
				}
				else if (properties.IsRightButtonPressed)
				{
					e.Handled = true;
					LongClick?.Invoke();
				}
			}, RoutingStrategies.Tunnel, handledEventsToo: true);
			AddHandler(PointerReleasedEvent, (_, _) => timer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
			AddHandler(PointerCaptureLostEvent, (_, _) => timer.Stop(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
		}

		protected override void OnClick()
		{
			if (longClicked)
			{
				longClicked = false;
				return; // Was a longclick: no click.
			}

			base.OnClick();
		}
	}
}
