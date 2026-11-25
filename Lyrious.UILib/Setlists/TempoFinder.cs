using System;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Finds a recording's tempo and where its beats fall, from how loud it is over time (see
	/// <see cref="SoundWave"/>): where it gets louder (a note or a drum hit starting) tends to repeat
	/// at the beat, so the beat is the repeat that fits those moments best.
	/// </summary>
	public static class TempoFinder
	{
		/// <summary>The tempo found (beats per minute) and a beat's time (seconds from the sound's start).</summary>
		public sealed record Found(double Tempo, double Beat);

		// The tempos looked for, and the one preferred among those that fit as well (half and double
		// tempo often fit almost as well as the right one).
		private const double SlowestTempo = 60;
		private const double FastestTempo = 200;
		private const double LikeliestTempo = 120;

		/// <summary>
		/// The tempo between <paramref name="from"/> and <paramref name="to"/> (seconds), and the beat
		/// nearest <paramref name="near"/> (e.g. the song's start set by hand); null when that's too short
		/// (under 4 seconds) or there are no beats to find (silence).
		/// </summary>
		public static Found? Find(SoundWave wave, double from, double to, double near)
		{
			var rate = wave.Rate;
			var first = Math.Clamp((int)(from * rate), 1, wave.Levels.Length);
			var last = Math.Clamp((int)(to * rate), first, wave.Levels.Length);
			var length = last - first;
			if (length < 4 * rate)
			{
				return null;
			}

			// How much louder each slice is than the one before (on a loudness scale, as the ear hears).
			var rises = new double[length];
			for (var i = 0; i < length; i++)
			{
				var now = Math.Log(1e-3 + wave.Levels[first + i]);
				var before = Math.Log(1e-3 + wave.Levels[first + i - 1]);
				rises[i] = Math.Max(0, now - before);
			}

			// Without its average, so a loud stretch doesn't count as a beat everywhere.
			var mean = 0.0;
			foreach (var rise in rises)
			{
				mean += rise;
			}

			mean /= length;
			for (var i = 0; i < length; i++)
			{
				rises[i] = Math.Max(0, rises[i] - mean);
			}

			// The repeat (in slices) that fits the rises best, a little in favour of tempos near the likeliest.
			var shortest = (int)Math.Floor(60.0 * rate / FastestTempo);
			var longest = (int)Math.Ceiling(60.0 * rate / SlowestTempo);
			var fits = new double[longest + 2];
			var bestLag = 0;
			var bestScore = 0.0;
			for (var lag = shortest; lag <= longest + 1 && lag < length; lag++)
			{
				var sum = 0.0;
				for (var i = 0; i + lag < length; i++)
				{
					sum += rises[i] * rises[i + lag];
				}

				fits[lag] = sum / (length - lag);
				var tempo = 60.0 * rate / lag;
				var octaves = Math.Log2(tempo / LikeliestTempo);
				var score = fits[lag] * Math.Exp(-0.5 * octaves * octaves / (0.9 * 0.9));
				if (lag <= longest && score > bestScore)
				{
					bestScore = score;
					bestLag = lag;
				}
			}

			if (bestLag == 0 || bestScore <= 0)
			{
				return null;
			}

			// Between whole slices: the peak of a curve through the best and its neighbours.
			var period = (double)bestLag;
			if (bestLag > shortest && bestLag + 1 < fits.Length)
			{
				var (a, b, c) = (fits[bestLag - 1], fits[bestLag], fits[bestLag + 1]);
				var bend = a - 2 * b + c;
				if (bend < 0)
				{
					period += Math.Clamp(0.5 * (a - c) / bend, -0.5, 0.5);
				}
			}

			// Where the beats fall: the offset (within one beat) whose beats land on the most rises.
			var bestPhase = 0.0;
			var bestSum = -1.0;
			for (var phase = 0.0; phase < period; phase += 1)
			{
				var sum = 0.0;
				for (var at = phase; at < length; at += period)
				{
					var i = (int)Math.Round(at);
					if (i < length)
					{
						// The slice and its neighbours: a beat is rarely exactly on one slice.
						sum += rises[i] + 0.5 * ((i > 0 ? rises[i - 1] : 0) + (i + 1 < length ? rises[i + 1] : 0));
					}
				}

				if (sum > bestSum)
				{
					bestSum = sum;
					bestPhase = phase;
				}
			}

			var seconds = period / rate;
			var beat = (first + bestPhase) / rate;
			// The beat nearest the time given.
			beat += Math.Round((near - beat) / seconds) * seconds;
			return new Found(60.0 / seconds, Math.Max(0, beat));
		}
	}
}
