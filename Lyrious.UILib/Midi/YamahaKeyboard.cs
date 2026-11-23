using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib
{
	/// <summary>
	/// A user sound of a Yamaha keyboard: its bank (a YC's or CK's Live Set page 1–20, a MODX's user bank
	/// 1–5), its number in the bank (a Live Set button 1–8, a MODX performance 1–128) and its name.
	/// </summary>
	public sealed record KeyboardSound(int Bank, int Number, string Name);

	/// <summary>
	/// A Yamaha keyboard over MIDI (each model's Owner's Manual or Data List): its user sounds, read with
	/// a bulk dump request each (their names are at the start of the Common block), and how each is
	/// chosen by bank select and program change. The sounds read last are kept on this device, per model.
	/// <list type="bullet">
	/// <item>YC61/YC73/YC88: 20 Live Set pages of 8 sounds; model ID 09; MSB 63, LSB the page, program the button.</item>
	/// <item>CK61/CK76/CK88: the same as the YC, with model ID 0B.</item>
	/// <item>MODX: 5 user banks of 128 Performances; model ID 07; MSB 64 (the whole Performance), LSB 32–36, program the number.</item>
	/// </list>
	/// </summary>
	public sealed class YamahaKeyboard
	{
		/// <summary>The YC61, YC73 and YC88 stage keyboards.</summary>
		public static readonly YamahaKeyboard Yc = new("YC", "YC61 / YC73 / YC88", modelId: 0x09, banks: 20, perBank: 8, nameLength: 15,
			common: [0x46, 0x00, 0x00], header: (bank, number) => [0x0E, (byte)(bank - 1), (byte)(number - 1)],
			msb: 63, lsb: bank => bank - 1, isLiveSet: true);

		/// <summary>The CK61, CK76 and CK88 stage keyboards.</summary>
		public static readonly YamahaKeyboard Ck = new("CK", "CK61 / CK76 / CK88", modelId: 0x0B, banks: 20, perBank: 8, nameLength: 15,
			common: [0x46, 0x00, 0x00], header: (bank, number) => [0x0E, (byte)(bank - 1), (byte)(number - 1)],
			msb: 63, lsb: bank => bank - 1, isLiveSet: true);

		/// <summary>The MODX synthesizers (their user Performances).</summary>
		public static readonly YamahaKeyboard Modx = new("MODX", "MODX", modelId: 0x07, banks: 5, perBank: 128, nameLength: 20,
			common: [0x30, 0x40, 0x00], header: (bank, number) => [0x0E, (byte)(0x20 + bank - 1), (byte)(number - 1)],
			msb: 64, lsb: bank => 32 + bank - 1, isLiveSet: false);

		/// <summary>The keyboards whose sounds can be read.</summary>
		public static readonly IReadOnlyList<YamahaKeyboard> All = [Yc, Ck, Modx];

		// The bulk dump's address: its high byte (after F0 43 0n 7F 1C bh bl id).
		private const int AddressIndex = 8;
		private const byte BulkFooter = 0x0F;
		// A MODX bank stops being read after this many empty (initialized) Performances in a row.
		private const int EmptyInARowToStop = 8;

		private readonly byte modelId;
		private readonly int nameLength;
		private readonly byte[] common;
		private readonly Func<int, int, byte[]> header;
		private readonly int msb;
		private readonly Func<int, int> lsb;
		private readonly bool isLiveSet;

		private YamahaKeyboard(string id, string name, byte modelId, int banks, int perBank, int nameLength, byte[] common, Func<int, int, byte[]> header, int msb, Func<int, int> lsb, bool isLiveSet)
		{
			Id = id;
			Name = name;
			this.modelId = modelId;
			Banks = banks;
			PerBank = perBank;
			this.nameLength = nameLength;
			this.common = common;
			this.header = header;
			this.msb = msb;
			this.lsb = lsb;
			this.isLiveSet = isLiveSet;
		}

		/// <summary>Its short name, as kept ("YC", "CK", "MODX").</summary>
		public string Id { get; }

		/// <summary>Its name as shown ("YC61 / YC73 / YC88").</summary>
		public string Name { get; }

		/// <summary>How many banks of user sounds it has.</summary>
		public int Banks { get; }

		/// <summary>How many sounds a bank has.</summary>
		public int PerBank { get; }

		/// <summary>The keyboard by its short name; the YC for none or another.</summary>
		public static YamahaKeyboard ById(string? id) => All.FirstOrDefault(keyboard => keyboard.Id == id) ?? Yc;

		/// <summary>The MIDI that selects a sound, on the keyboard's receive channel.</summary>
		public MidiSound Midi(KeyboardSound sound, int channel) => new(channel, msb, lsb(sound.Bank), sound.Number - 1, sound.Name);

		/// <summary>Whether the MIDI selects that sound of this keyboard.</summary>
		public bool Selects(MidiSound midi, KeyboardSound sound)
			=> midi.BankMsb == msb && midi.BankLsb == lsb(sound.Bank) && midi.Program == sound.Number - 1;

		/// <summary>Where a sound is on the keyboard: "3-5" (page 3, button 5) on a YC or CK, "User 2: 17" on a MODX.</summary>
		public string Place(KeyboardSound sound) => isLiveSet ? $"{sound.Bank}-{sound.Number}" : L.T("User {0}: {1}", sound.Bank, sound.Number);

		/// <summary>Where the sound chosen by the MIDI is on this keyboard ("3-5", "User 2: 17"); null when it isn't one of its user sounds.</summary>
		public string? Place(MidiSound midi)
		{
			for (var bank = 1; bank <= Banks; bank++)
			{
				if (midi.BankMsb == msb && midi.BankLsb == lsb(bank) && midi.Program < PerBank)
				{
					return Place(new KeyboardSound(bank, midi.Program + 1, midi.Name));
				}
			}

			return null;
		}

		private string CacheKey => $"keyboard-sounds-{Id.ToLowerInvariant()}";

		/// <summary>The sounds read last from this model (on this device); none before the first time.</summary>
		public IReadOnlyList<KeyboardSound> Kept
		{
			get
			{
				try
				{
					return App.Settings.Get(CacheKey) is { Length: > 0 } json
						? JsonSerializer.Deserialize(json, KeyboardJson.Default.KeyboardSoundArray) ?? []
						: [];
				}
				catch (JsonException)
				{
					return [];
				}
			}
			private set => App.Settings.Set(CacheKey, value.Count == 0 ? null : JsonSerializer.Serialize(value.ToArray(), KeyboardJson.Default.KeyboardSoundArray));
		}

		/// <summary>
		/// Reads the user sounds' names from the keyboard, one at a time (<paramref name="progress"/> gets
		/// how many are done, of <see cref="Banks"/> × <see cref="PerBank"/>), and keeps them. A MODX bank
		/// stops at a run of empty (initialized) Performances, the rest of it taken to be empty too. Null
		/// when the keyboard doesn't answer the first one.
		/// </summary>
		public async Task<IReadOnlyList<KeyboardSound>?> ReadSoundsAsync(IMidiRequester midi, IProgress<int>? progress, CancellationToken cancellation)
		{
			var sounds = new List<KeyboardSound>();
			var done = 0;
			for (var bank = 1; bank <= Banks; bank++)
			{
				var emptyInARow = 0;
				for (var number = 1; number <= PerBank; number++)
				{
					cancellation.ThrowIfCancellationRequested();
					var first = done == 0;
					var replies = await midi.RequestAsync(Request(bank, number), AddressIndex, BulkFooter, TimeSpan.FromMilliseconds(first ? 2000 : 1500));
					var name = replies.Select(NameIn).FirstOrDefault(found => found != null);
					if (name == null && first)
					{
						return null; // No such keyboard answering.
					}

					done++;
					progress?.Report(done);
					var empty = string.IsNullOrWhiteSpace(name) || name.StartsWith("Init", StringComparison.OrdinalIgnoreCase);
					if (isLiveSet || !empty)
					{
						sounds.Add(new KeyboardSound(bank, number, name ?? ""));
					}

					emptyInARow = empty ? emptyInARow + 1 : 0;
					if (!isLiveSet && emptyInARow >= EmptyInARowToStop)
					{
						done += PerBank - number;
						progress?.Report(done);
						break; // The rest of the bank is taken to be empty.
					}
				}
			}

			Kept = sounds;
			return sounds;
		}

		/// <summary>The bulk dump request of a user sound (device number 1).</summary>
		private byte[] Request(int bank, int number)
			=> [0xF0, 0x43, 0x20, 0x7F, 0x1C, modelId, .. header(bank, number), 0xF7];

		/// <summary>The sound's name in a bulk dump of its Common block (the name its first bytes); else null.</summary>
		private string? NameIn(byte[] message)
		{
			// F0 43 0n 7F 1C bh bl id ah am al [data…] cc F7
			if (message.Length < 11 + nameLength + 2 || message[0] != 0xF0 || message[1] != 0x43 || (message[2] & 0xF0) != 0x00
			    || message[3] != 0x7F || message[4] != 0x1C || message[7] != modelId
			    || message[AddressIndex] != common[0] || message[AddressIndex + 1] != common[1] || message[AddressIndex + 2] != common[2])
			{
				return null;
			}

			var name = new StringBuilder();
			for (var i = 11; i < 11 + nameLength && message[i] != 0; i++)
			{
				var c = message[i];
				name.Append(c is >= 0x20 and < 0x7F ? (char)c : ' ');
			}

			return name.ToString().Trim();
		}
	}

	[System.Text.Json.Serialization.JsonSerializable(typeof(KeyboardSound[]))]
	internal sealed partial class KeyboardJson : System.Text.Json.Serialization.JsonSerializerContext;
}
