using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib
{
	/// <summary>Shows the app full screen, where the platform can: the desktop's window, or the browser's page.</summary>
	public interface IFullScreenHost
	{
		/// <summary>Whether the app can be shown full screen here (e.g. not in Safari on iPhone).</summary>
		bool IsSupported { get; }

		/// <summary>Shows the app full screen, or not. A browser does it at the next tap or key if it can't at once.</summary>
		void Set(bool on);

		/// <summary>Raised when full screen was left otherwise than by <see cref="Set"/> (e.g. Escape in the browser).</summary>
		event Action? Exited;
	}

	/// <summary>
	/// The app full screen as the member chose (see <see cref="DisplaySettings.FullScreen"/>, kept with the
	/// user): applied by the platform's <see cref="Host"/> when it's there, when the setting changes (here
	/// or on another device), and turned off when full screen is left otherwise (so it doesn't come back).
	/// F11 turns it on and off.
	/// </summary>
	public static class FullScreen
	{
		private static IFullScreenHost? host;

		/// <summary>The platform's way of showing the app full screen (set by the desktop's window or the browser host); null for none.</summary>
		public static IFullScreenHost? Host
		{
			get => host;
			set
			{
				if (host != null)
				{
					host.Exited -= OnExited;
				}

				host = value;
				if (host != null)
				{
					host.Exited += OnExited;
					Apply();
				}
			}
		}

		/// <summary>Whether the app can be shown full screen here.</summary>
		public static bool IsSupported => host?.IsSupported == true;

		/// <summary>Called once at startup: follows the setting, and F11 on every window (and the browser's root).</summary>
		public static void Register()
		{
			DisplaySettings.FullScreenChanged += Apply;
			UserSettings.Changed += () => Dispatcher.UIThread.Post(Apply);
			InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, e) =>
			{
				if (e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.None && !e.Handled && IsSupported)
				{
					e.Handled = true;
					DisplaySettings.FullScreen = !DisplaySettings.FullScreen;
				}
			});
		}

		private static void Apply()
		{
			if (IsSupported)
			{
				host!.Set(DisplaySettings.FullScreen);
			}
		}

		// Left by the member (Escape, the system): the setting off, or it'd be full screen again at once.
		private static void OnExited() => Dispatcher.UIThread.Post(() =>
		{
			if (DisplaySettings.FullScreen)
			{
				DisplaySettings.FullScreen = false;
			}
		});
	}
}
