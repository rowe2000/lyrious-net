using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Keeps songs' sound files (see <see cref="ISoundStore"/>) on the server too, so a file added on
	/// one device plays on the band's others. A file added here is sent at once, or, when that fails
	/// (offline, or the song hasn't reached the server yet), kept in a list on this device and sent
	/// when the server can be reached again (<see cref="Watch"/>). A file that isn't on this device
	/// is fetched from the server when it's played (<see cref="FetchAsync"/>).
	/// </summary>
	public static class SoundSync
	{
		// "<song id>/<sound id>" per line: the files still to send, and the song each is for.
		private const string PendingKey = "pending-sound-uploads";

		private static readonly SemaphoreSlim Sending = new(1, 1);
		private static ISetlistService? watched;

		/// <summary>Raised (on any thread) when the files waiting to be sent change.</summary>
		public static event Action? PendingChanged;

		private readonly record struct Pending(Guid SongId, string SoundId);

		/// <summary>The ids of the sound files that haven't reached the server yet.</summary>
		public static IReadOnlySet<string> PendingSoundIds => Load().Select(pending => pending.SoundId).ToHashSet();

		/// <summary>
		/// Sends the files waiting whenever the service's status changes (back online, or its own
		/// changes sent, e.g. the song a file is for), and now.
		/// </summary>
		public static void Watch(ISetlistService service)
		{
			if (watched != service)
			{
				if (watched != null)
				{
					watched.StatusChanged -= OnStatusChanged;
				}

				watched = service;
				service.StatusChanged += OnStatusChanged;
			}

			_ = SendPendingAsync();
		}

		private static void OnStatusChanged()
		{
			if (watched?.Status.Online == true)
			{
				_ = SendPendingAsync();
			}
		}

		/// <summary>Sends a file just kept on this device, for the song: now, or once the server can be reached.</summary>
		public static void Upload(Guid songId, string soundId)
		{
			var pending = Load();
			if (!pending.Contains(new Pending(songId, soundId)))
			{
				pending.Add(new Pending(songId, soundId));
				Save(pending);
			}

			_ = SendPendingAsync();
		}

		// The sound files being fetched: asked for again meanwhile (e.g. played while it's fetched ahead),
		// the same fetch is waited for, so the file isn't written twice at once.
		private static readonly Dictionary<string, Task<bool>> Fetching = [];

		/// <summary>
		/// Fetches the sound file from the server and keeps it on this device. False when the server
		/// hasn't got it; throws when it can't be reached.
		/// </summary>
		public static Task<bool> FetchAsync(string soundId)
		{
			lock (Fetching)
			{
				if (Fetching.TryGetValue(soundId, out var running))
				{
					return running;
				}

				var fetch = FetchNowAsync(soundId);
				Fetching[soundId] = fetch;
				_ = fetch.ContinueWith(_ =>
				{
					lock (Fetching)
					{
						Fetching.Remove(soundId);
					}
				}, TaskScheduler.Default);
				return fetch;
			}
		}

		private static async Task<bool> FetchNowAsync(string soundId)
		{
			if (App.SoundStore is not { } sounds)
			{
				return false;
			}

			await using var content = await App.SetlistService.DownloadSoundAsync(soundId);
			if (content == null)
			{
				return false;
			}

			await sounds.SaveAsync(soundId, content);
			return true;
		}

		/// <summary>Sends the files waiting, one at a time; those that can't be sent yet stay in the list.</summary>
		public static async Task SendPendingAsync()
		{
			if (App.SoundStore is not { } sounds || !await Sending.WaitAsync(0))
			{
				return; // Already sending: it'll get to them.
			}

			try
			{
				foreach (var pending in Load())
				{
					try
					{
						await using var content = await sounds.OpenAsync(pending.SoundId);
						if (content != null)
						{
							await App.SetlistService.UploadSoundAsync(pending.SongId, pending.SoundId, content);
						}

						// Sent, or no longer on this device (deleted): nothing left to send.
						var left = Load();
						left.Remove(pending);
						Save(left);
					}
					catch (Exception)
					{
						// Offline, or the song isn't on the server yet: tried again on the next change.
					}
				}
			}
			finally
			{
				Sending.Release();
			}
		}

		private static List<Pending> Load()
		{
			var list = new List<Pending>();
			foreach (var line in (App.Settings.Get(PendingKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var slash = line.IndexOf('/');
				if (slash > 0 && Guid.TryParse(line[..slash], out var songId) && slash < line.Length - 1)
				{
					list.Add(new Pending(songId, line[(slash + 1)..]));
				}
			}

			return list;
		}

		private static void Save(List<Pending> pending)
		{
			App.Settings.Set(PendingKey, pending.Count == 0 ? null : string.Join("\n", pending.Select(p => $"{p.SongId}/{p.SoundId}")));
			PendingChanged?.Invoke();
		}
	}
}
