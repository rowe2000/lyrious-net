using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Lyrious.UILib
{
	/// <summary>
	/// Sharing on the local network (the Sync page): a web server in this app, through which
	/// browsers on other devices on the same network open the web app and reach the server this app
	/// syncs with, even one only this computer can reach (e.g. http://localhost:5180). Hosts that
	/// can listen on the network (the desktop app) supply one through <see cref="App.LanSharing"/>;
	/// elsewhere it isn't offered.
	/// </summary>
	public interface ILanSharing
	{
		bool IsRunning { get; }

		/// <summary>What to open in a browser on another device, e.g. http://192.168.1.20:5190; one per network this computer is on.</summary>
		IReadOnlyList<string> Addresses { get; }

		/// <summary>
		/// Starts listening on the port; nothing when already running on it, and moves there when
		/// running on another. Fails with a message for the user (e.g. the port is taken).
		/// </summary>
		Task StartAsync(int port);

		Task StopAsync();

		/// <summary>Raised, on any thread, when sharing has started, stopped or moved to another port.</summary>
		event Action? Changed;
	}

	/// <summary>Whether the app is shared on the local network and on which port, kept in <see cref="App.Settings"/>.</summary>
	public static class LanSharingSettings
	{
		public const int DefaultPort = 5190;

		// Below 1024 a program usually isn't allowed to listen.
		public const int MinPort = 1024;
		public const int MaxPort = 65535;

		/// <summary>Off unless turned on; on, the app shares again the next time it starts.</summary>
		public static bool Enabled
		{
			get => App.Settings.Get(SettingKeys.LanSharing) == "true";
			set => App.Settings.Set(SettingKeys.LanSharing, value ? "true" : null);
		}

		public static int Port
		{
			get => int.TryParse(App.Settings.Get(SettingKeys.LanSharingPort), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
			       && port is >= MinPort and <= MaxPort
				? port
				: DefaultPort;
			set
			{
				var port = Math.Clamp(value, MinPort, MaxPort);
				App.Settings.Set(SettingKeys.LanSharingPort, port == DefaultPort ? null : port.ToString(CultureInfo.InvariantCulture));
			}
		}
	}
}
