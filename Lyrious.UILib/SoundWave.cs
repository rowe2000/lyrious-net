using System;
using System.IO;
using Concentus;
using Concentus.Oggfile;

namespace Lyrious.UILib
{
	/// <summary>
	/// How loud a sound is over time, for drawing its waveform and finding its beats: the loudest a
	/// sample gets (0 to 1, all channels) in each slice of 1/<see cref="Rate"/> second, from the start.
	/// </summary>
	public sealed record SoundWave(int Rate, float[] Levels)
	{
		/// <summary>The slices per second the levels are measured in.</summary>
		public const int DefaultRate = 200;

		/// <summary>How long the sound is, in seconds.</summary>
		public double Seconds => Levels.Length / (double)Rate;

		/// <summary>
		/// The sound's levels from an Ogg file with Opus in it (as the app records), decoded here, so it
		/// works where the device can't decode Ogg itself. Null when it isn't one.
		/// </summary>
		public static SoundWave? FromOggOpus(Stream content)
		{
			try
			{
				var decoder = OpusCodecFactory.CreateDecoder(48000, 2);
				var ogg = new OpusOggReadStream(decoder, content);
				var builder = new SoundWaveBuilder(48000, 2);
				while (ogg.HasNextPacket)
				{
					if (ogg.DecodeNextPacket() is { } packet)
					{
						foreach (var sample in packet)
						{
							builder.Add(sample / 32768f);
						}
					}
				}

				return builder.Count > 0 ? builder.Build() : null;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Not an Ogg Opus sound: {ex.Message}");
				return null;
			}
		}
	}

	/// <summary>
	/// Makes a <see cref="SoundWave"/> from a sound's samples as they're decoded (interleaved channels,
	/// -1 to 1), keeping only each slice's loudest one.
	/// </summary>
	public sealed class SoundWaveBuilder
	{
		private readonly System.Collections.Generic.List<float> levels = [];
		private readonly double samplesPerSlice;
		private readonly int channels;
		private long index;
		private float loudest;
		private long sliceEnd;

		/// <param name="sampleRate">The sound's samples per second (per channel).</param>
		/// <param name="channels">How many channels the samples are interleaved from.</param>
		/// <param name="rate">The slices per second of the levels.</param>
		public SoundWaveBuilder(double sampleRate, int channels, int rate = SoundWave.DefaultRate)
		{
			this.channels = Math.Max(1, channels);
			Rate = rate;
			samplesPerSlice = Math.Max(1, sampleRate / rate) * this.channels;
			sliceEnd = (long)Math.Round(samplesPerSlice);
		}

		/// <summary>The slices per second.</summary>
		public int Rate { get; }

		/// <summary>The slices so far.</summary>
		public int Count => levels.Count;

		/// <summary>The next sample (of the next channel in turn).</summary>
		public void Add(float sample)
		{
			var size = Math.Abs(sample);
			if (size > loudest)
			{
				loudest = size;
			}

			if (++index >= sliceEnd)
			{
				levels.Add(Math.Min(1, loudest));
				loudest = 0;
				sliceEnd = (long)Math.Round((levels.Count + 1) * samplesPerSlice);
			}
		}

		/// <summary>The levels of the samples added (a last part slice included).</summary>
		public SoundWave Build()
		{
			if (loudest > 0)
			{
				levels.Add(Math.Min(1, loudest));
				loudest = 0;
			}

			return new SoundWave(Rate, levels.ToArray());
		}
	}
}
