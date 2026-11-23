using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lyrious.UILib
{
	/// <summary>
	/// A sound on the member's keyboard, chosen for a song (kept in their own settings for it, see
	/// <see cref="Setlists.MySongSetting.MidiSound"/>): the MIDI channel (1–16), bank (MSB, LSB) and
	/// program (0–127) that select it, and its name as the keyboard calls it. Stored as
	/// "channel,msb,lsb,program,name".
	/// </summary>
	public sealed record MidiSound(int Channel, int BankMsb, int BankLsb, int Program, string Name)
	{
		/// <summary>The sound stored as text (see <see cref="Parse"/>).</summary>
		public string Format() => string.Join(',',
			Channel.ToString(CultureInfo.InvariantCulture),
			BankMsb.ToString(CultureInfo.InvariantCulture),
			BankLsb.ToString(CultureInfo.InvariantCulture),
			Program.ToString(CultureInfo.InvariantCulture),
			Name);

		/// <summary>The sound stored as text ("channel,msb,lsb,program,name"); null for none or something else.</summary>
		public static MidiSound? Parse(string? text)
		{
			var parts = (text ?? "").Split(',', 5);
			if (parts.Length < 4
			    || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var channel) || channel is < 1 or > 16
			    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var msb) || msb > 127
			    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var lsb) || lsb > 127
			    || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var program) || program > 127)
			{
				return null;
			}

			return new MidiSound(channel, msb, lsb, program, parts.Length > 4 ? parts[4] : "");
		}

		/// <summary>The MIDI that selects it: bank select (MSB, then LSB) and the program change, on its channel.</summary>
		public IReadOnlyList<byte[]> Messages()
		{
			var channel = (byte)(Math.Clamp(Channel, 1, 16) - 1);
			return
			[
				[(byte)(0xB0 | channel), 0x00, (byte)BankMsb],
				[(byte)(0xB0 | channel), 0x20, (byte)BankLsb],
				[(byte)(0xC0 | channel), (byte)Program],
			];
		}
	}

	/// <summary>
	/// Asks the instruments on the MIDI outputs for something and listens on the MIDI inputs for their
	/// System Exclusive answers (e.g. a keyboard's sounds, see <see cref="Yc61"/>). Set by hosts that can
	/// (with <see cref="App.Midi"/>, which then implements it too).
	/// </summary>
	public interface IMidiRequester
	{
		/// <summary>
		/// Sends <paramref name="request"/> on every MIDI output and gathers the System Exclusive messages
		/// that come back on the inputs, until one with <paramref name="lastValue"/> at
		/// <paramref name="lastIndex"/> (the last of the answer) or <paramref name="timeout"/> without it.
		/// </summary>
		System.Threading.Tasks.Task<IReadOnlyList<byte[]>> RequestAsync(byte[] request, int lastIndex, byte lastValue, TimeSpan timeout);
	}
}
