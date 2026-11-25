using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Records the band while a song plays, when this device records plays (a device setting, see
	/// <see cref="PlaybackSettings.RecordPlays"/>): from pressing play until it stops, the device's
	/// sound input in two channels (see <see cref="IAudioRecorder"/>), made into an OGG file (Opus)
	/// as it's recorded. Then it's kept in Sound files on this device, named after the song and when it
	/// was played, kept with the play in the band's playlog, and sent to the server for the band's
	/// other devices (see <see cref="SoundSync"/>).
	/// </summary>
	public sealed class PlayRecorder(ISetlistService service, Action<string> notice)
	{
		// Shorter than this, it's taken for a slip (play pressed and stopped again), not kept.
		private static readonly TimeSpan Shortest = TimeSpan.FromSeconds(3);

		// As long as a play's recording link may be (Play.MaxRecordingLength on the server).
		private const int MaxRecordingLength = 300;

		private Recording? recording;

		/// <summary>Whether this device can record plays (it has a sound input and keeps sound files).</summary>
		public static bool IsSupported => App.AudioRecorder != null && App.SoundStore != null;

		/// <summary>Starts recording the song just started, if this device records plays.</summary>
		/// <param name="play">The play, as it's being added to the playlog (null when it couldn't be).</param>
		public void Start(Guid songId, string songName, Task<Guid?> play)
		{
			if (!PlaybackSettings.RecordPlays || !IsSupported || recording != null)
			{
				return;
			}

			var started = new Recording(songId, songName, play, DateTime.Now);
			recording = started;
			started.Starting = StartAsync(started);
		}

		/// <summary>Stops recording (when the song stops, or is left), and keeps what was recorded.</summary>
		public async Task StopAsync()
		{
			if (recording is not { } stopped)
			{
				return;
			}

			recording = null;
			try
			{
				if (!await stopped.Starting)
				{
					return;
				}

				await App.AudioRecorder!.StopAsync();
				byte[] file;
				TimeSpan length;
				lock (stopped)
				{
					stopped.Ogg!.Finish();
					file = stopped.Output.ToArray();
					length = TimeSpan.FromSeconds((double)stopped.Samples / 2 / stopped.SampleRate);
				}

				if (length < Shortest)
				{
					return;
				}

				var name = $"{stopped.SongName} {stopped.Started:yyyy-MM-dd HH.mm}.ogg";
				var sound = new SoundFile(MediaLinks.NewSoundId(name), name);
				await App.SoundStore!.SaveAsync(sound.Id, new MemoryStream(file));
				if (await stopped.Play is { } playId)
				{
					await service.SetPlayRecordingAsync(playId, MediaLinks.SoundLink(sound, MaxRecordingLength));
				}

				SoundSync.Upload(stopped.SongId, sound.Id);
				notice(L.T("Recorded {0}: it's in Sound files and the playlog", stopped.SongName));
			}
			catch (Exception ex)
			{
				notice(L.T("The recording couldn't be kept. {0}", ex.Message));
			}
		}

		/// <summary>Starts the device recording; false (and why, as a notice) when it can't.</summary>
		private async Task<bool> StartAsync(Recording started)
		{
			try
			{
				var sampleRate = await App.AudioRecorder!.StartAsync(PlaybackSettings.RecordingInput, (samples, count) => Write(started, samples, count));
				lock (started)
				{
					var encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
					// As much compressed as chosen in Settings (128 kbit/s unless changed).
					encoder.Bitrate = PlaybackSettings.RecordingBitrate * 1000;
					var tags = new OpusTags();
					tags.Fields[OpusTagName.Title] = started.SongName;
					// Any sample rate: the writer makes it Opus's 48 kHz itself.
					started.Ogg = new OpusOggWriteStream(encoder, started.Output, tags, sampleRate);
					started.SampleRate = sampleRate;

					// What came before the rate was known.
					foreach (var waiting in started.Waiting)
					{
						started.Ogg.WriteSamples(waiting, 0, waiting.Length);
						started.Samples += waiting.Length;
					}

					started.Waiting.Clear();
				}

				return true;
			}
			catch (Exception ex)
			{
				if (recording == started)
				{
					recording = null;
				}

				notice(L.T("Couldn't record the song. {0}", ex.Message));
				return false;
			}
		}

		/// <summary>New sound from the device (any thread): encoded as it comes.</summary>
		private static void Write(Recording to, short[] samples, int count)
		{
			lock (to)
			{
				if (to.Ogg == null)
				{
					to.Waiting.Add(samples[..count]);
					return;
				}

				to.Ogg.WriteSamples(samples, 0, count);
				to.Samples += count;
			}
		}

		/// <summary>A recording under way: the song, its play, and the file it's becoming.</summary>
		private sealed class Recording(Guid songId, string songName, Task<Guid?> play, DateTime started)
		{
			public Guid SongId { get; } = songId;
			public string SongName { get; } = songName;
			public Task<Guid?> Play { get; } = play;
			public DateTime Started { get; } = started;
			public Task<bool> Starting { get; set; } = Task.FromResult(false);
			public MemoryStream Output { get; } = new();
			public OpusOggWriteStream? Ogg { get; set; }
			public int SampleRate { get; set; } = 48000;
			public long Samples { get; set; }
			public List<short[]> Waiting { get; } = [];
		}
	}
}
