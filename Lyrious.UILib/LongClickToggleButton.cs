using System;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Lyrious.UILib
{
	/// <summary>
	/// A toggle button that also has a longclick: pressed and held for <see cref="Duration"/> (mouse or
	/// touch). A longclick raises <see cref="LongClick"/> instead of the ordinary click, so the button
	/// doesn't also toggle when it's let go.
	/// </summary>
	public sealed class LongClickToggleButton : ToggleButton
	{
		/// <summary>How long a press must be held to be a longclick.</summary>
		public static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);

		private readonly DispatcherTimer timer = new() { Interval = Duration };

		// The press became a longclick: the click when it's let go is left out.
		private bool longClicked;

		/// <summary>Raised when the button has been held for <see cref="Duration"/> (while still held).</summary>
		public event Action? LongClick;

		// Styled as an ordinary toggle button.
		protected override Type StyleKeyOverride => typeof(ToggleButton);

		public LongClickToggleButton()
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
				if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
				{
					longClicked = false;
					timer.Start();
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
				return; // Was a longclick: no toggle.
			}

			base.OnClick();
		}
	}
}
