using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib
{
	/// <summary>
	/// The settings that go with the user rather than the device (the colours, see
	/// <see cref="ColorSettings"/>): kept on the server with their account (see
	/// <see cref="ISetlistService.GetMySettingsAsync"/>), so they're the same on all their devices.
	/// <para>
	/// <see cref="App.Settings"/> keeps the last known copy, so they show before logging in and while
	/// offline. After login the server's copy wins; if the user has never saved any, this device's are
	/// sent instead, so colours picked before they went with the user aren't lost. A change made here
	/// is sent shortly after (once, for a run of changes, e.g. dragging in the colour picker), or once
	/// the server can be reached again; a change made on another device shows here at once.
	/// </para>
	/// </summary>
	public static class UserSettings
	{
		/// <summary>The settings kept with the user.</summary>
		private static readonly string[] Keys =
		[
			SettingKeys.HighlightColor,
			SettingKeys.ChordColor,
			SettingKeys.Heading1Color,
			SettingKeys.Heading2Color,
			SettingKeys.HeadingColor,
			SettingKeys.Language,
			SettingKeys.Theme,
			SettingKeys.CapoMode,
			SettingKeys.TransposeButton,
			SettingKeys.ChordGridButton,
			SettingKeys.SendYamahaTranspose,
			SettingKeys.Midi,
			SettingKeys.MidiExpressions,
			SettingKeys.MidiCommands,
		];

		/// <summary>How long after the last change it's sent.</summary>
		private static readonly TimeSpan UploadDelay = TimeSpan.FromMilliseconds(600);

		private static ISetlistService? service;
		private static string? userName;
		private static bool wasOnline;
		private static DispatcherTimer? uploadTimer;

		// Counts the changes made here, so one made while sending isn't marked as sent.
		private static int version;

		/// <summary>Raised (on the UI thread) when the settings have changed on another device.</summary>
		public static event Action? Changed;

		/// <summary>After login: follows the user's settings on the server (until <see cref="Detach"/>).</summary>
		public static void Attach(ISetlistService setlistService, string user)
		{
			Detach();
			service = setlistService;
			userName = user;
			wasOnline = setlistService.Status.Online;
			setlistService.MySettingsChanged += OnChangedElsewhere;
			setlistService.StatusChanged += OnStatusChanged;
			_ = SyncAsync();
		}

		/// <summary>On logout: stops following the user's settings; the last known copy stays here.</summary>
		public static void Detach()
		{
			if (service != null)
			{
				service.MySettingsChanged -= OnChangedElsewhere;
				service.StatusChanged -= OnStatusChanged;
			}

			service = null;
			userName = null;
			uploadTimer?.Stop();
		}

		/// <summary>Called when one of the settings has been changed here: it's sent to the server shortly.</summary>
		public static void ChangedHere()
		{
			version++;
			if (userName == null)
			{
				return; // Not logged in: this device only.
			}

			// Waiting to be sent, also across a restart; for this user only.
			App.Settings.Set(SettingKeys.UserSettingsPending, userName);
			if (uploadTimer == null)
			{
				uploadTimer = new DispatcherTimer { Interval = UploadDelay };
				uploadTimer.Tick += (_, _) =>
				{
					uploadTimer.Stop();
					_ = UploadAsync();
				};
			}

			uploadTimer.Stop();
			uploadTimer.Start();
		}

		private static bool IsPending => userName != null && App.Settings.Get(SettingKeys.UserSettingsPending) == userName;

		/// <summary>Sends changes waiting here, else takes the server's copy (or gives it this device's, if it has none).</summary>
		private static async Task SyncAsync()
		{
			if (service is not { } current)
			{
				return;
			}

			try
			{
				if (IsPending)
				{
					await UploadAsync();
					return;
				}

				var settings = await current.GetMySettingsAsync();
				if (service != current || IsPending)
				{
					return; // Logged out, or changed here meanwhile.
				}

				if (settings == null)
				{
					// Never saved: this device's become the user's.
					if (Keys.Any(key => App.Settings.Get(key) != null))
					{
						await UploadAsync();
					}

					return;
				}

				Take(settings);
			}
			catch (Exception ex)
			{
				// Offline (or the server refused): the copy here stays; tried again once back online.
				System.Diagnostics.Debug.WriteLine($"Couldn't sync the user's settings: {ex.Message}");
			}
		}

		/// <summary>Sends this device's copy to the server; once sent, nothing is waiting any more.</summary>
		private static async Task UploadAsync()
		{
			if (service is not { } current || userName is not { } user)
			{
				return;
			}

			var sending = version;
			var settings = new JsonObject();
			foreach (var key in Keys)
			{
				if (App.Settings.Get(key) is { } value)
				{
					settings[key] = value;
				}
			}

			try
			{
				await current.SaveMySettingsAsync(settings.ToJsonString());
				if (sending == version && service == current && App.Settings.Get(SettingKeys.UserSettingsPending) == user)
				{
					App.Settings.Set(SettingKeys.UserSettingsPending, null);
				}
			}
			catch (Exception ex)
			{
				// Still waiting: sent once the server can be reached again.
				System.Diagnostics.Debug.WriteLine($"Couldn't send the user's settings: {ex.Message}");
			}
		}

		/// <summary>Puts the server's copy here and shows it.</summary>
		private static void Take(string json)
		{
			if (JsonNode.Parse(json) is not JsonObject settings)
			{
				return;
			}

			var changed = false;
			foreach (var key in Keys)
			{
				var value = settings[key] is JsonValue node && node.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
				if (App.Settings.Get(key) != value)
				{
					App.Settings.Set(key, value);
					changed = true;
				}
			}

			if (changed)
			{
				ColorSettings.Apply();
				// The theme chosen on another device.
				ThemeSettings.Apply();
				// The language chosen on another device (the screens are made anew if it changed).
				Localization.Language.ApplyChanged();
				Changed?.Invoke();
			}
		}

		private static void OnChangedElsewhere(string json) => Dispatcher.UIThread.Post(() =>
		{
			// A change made here and not yet sent wins: it's sent after this one.
			if (service != null && !IsPending)
			{
				Take(json);
			}
		});

		// Back online: send what's waiting, or catch up on changes made elsewhere meanwhile.
		private static void OnStatusChanged() => Dispatcher.UIThread.Post(() =>
		{
			if (service is not { } current)
			{
				return;
			}

			var online = current.Status.Online;
			if (online && !wasOnline)
			{
				_ = SyncAsync();
			}

			wasOnline = online;
		});
	}
}
