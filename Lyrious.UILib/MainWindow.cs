using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib
{
	/// <summary>
	/// The app's window (desktop). It opens where it was when the app last closed: the same place, size
	/// and maximized or not (see <see cref="SettingKeys.WindowPlacement"/>), unless that place is no
	/// longer on any screen (e.g. a screen that was unplugged); then where the system puts it.
	/// It's the desktop's way of showing the app full screen (see <see cref="FullScreen"/>).
	/// </summary>
	public class MainWindow : Window, IFullScreenHost
	{
		// The window's place and size while it was last neither maximized nor minimized, kept when it's
		// maximized, so it goes back to it.
		private PixelPoint normalPosition;
		private Size normalSize;

		// Maximized or not before full screen, to go back to; and whether a change of state is ours.
		private WindowState beforeFullScreen = WindowState.Normal;
		private bool settingState;

		public MainWindow()
		{
			Title = L.T("Lyrious");
			Content = UiScale.Wrap(new MainView());
			RestorePlacement();
			PositionChanged += (_, _) => RememberNormal();
			Resized += (_, _) => RememberNormal();
			Closing += (_, _) => SavePlacement();
			// Shown (and placed): full screen if the member wants it.
			Opened += (_, _) => FullScreen.Host = this;
		}

		public bool IsSupported => true;

		public event Action? Exited;

		public void Set(bool on)
		{
			if (on == (WindowState == WindowState.FullScreen))
			{
				return;
			}

			settingState = true;
			try
			{
				if (on)
				{
					beforeFullScreen = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
					WindowState = WindowState.FullScreen;
				}
				else
				{
					WindowState = beforeFullScreen;
				}
			}
			finally
			{
				settingState = false;
			}
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			// Out of full screen otherwise than by Set (e.g. by the system): the setting follows.
			if (change.Property == WindowStateProperty && !settingState
			    && change.OldValue is WindowState.FullScreen && change.NewValue is not WindowState.FullScreen)
			{
				Exited?.Invoke();
			}
		}

		private void RememberNormal()
		{
			if (WindowState == WindowState.Normal)
			{
				normalPosition = Position;
				normalSize = ClientSize;
			}
		}

		private void RestorePlacement()
		{
			var parts = App.Settings.Get(SettingKeys.WindowPlacement)?.Split(',');
			if (parts is not { Length: >= 4 }
			    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
			    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
			    || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
			    || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
			    || width < 200 || height < 200)
			{
				return;
			}

			normalPosition = new PixelPoint(x, y);
			normalSize = new Size(width, height);
			WindowStartupLocation = WindowStartupLocation.Manual;
			Position = normalPosition;
			Width = width;
			Height = height;
			var maximized = parts.Length > 4 && parts[4] == "max";

			// Once shown (the screens are known then): back where the system puts it if that place is on
			// no screen any more, and maximized again if it was.
			Opened += (_, _) =>
			{
				var titleBar = new PixelPoint(normalPosition.X + 40, normalPosition.Y + 10);
				if (Screens.All.Count > 0 && !Screens.All.Any(screen => screen.WorkingArea.Contains(titleBar)))
				{
					if (Screens.Primary is { } primary)
					{
						Position = new PixelPoint(primary.WorkingArea.X + 40, primary.WorkingArea.Y + 40);
					}
				}

				if (maximized)
				{
					WindowState = WindowState.Maximized;
				}
			};
		}

		private void SavePlacement()
		{
			RememberNormal();
			if (normalSize.Width < 200 || normalSize.Height < 200)
			{
				return;
			}

			var placement = string.Join(",",
				normalPosition.X.ToString(CultureInfo.InvariantCulture),
				normalPosition.Y.ToString(CultureInfo.InvariantCulture),
				Math.Round(normalSize.Width).ToString(CultureInfo.InvariantCulture),
				Math.Round(normalSize.Height).ToString(CultureInfo.InvariantCulture));
			// Full screen comes from its own setting; the placement keeps what it was before it.
			if (WindowState == WindowState.Maximized || (WindowState == WindowState.FullScreen && beforeFullScreen == WindowState.Maximized))
			{
				placement += ",max";
			}

			App.Settings.Set(SettingKeys.WindowPlacement, placement);
		}
	}
}
