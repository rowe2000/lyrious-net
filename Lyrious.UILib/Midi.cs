using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lyrious.UILib
{
	/// <summary>Sends MIDI messages; set by the host (see <see cref="App.Midi"/>).</summary>
	public interface IMidiOutput
	{
		/// <summary>Sends the messages, in order, on every MIDI output there is now. Never throws.</summary>
		void Send(IReadOnlyList<byte[]> messages);
	}

	/// <summary>A parameter of a <see cref="MidiExpression"/>: a name, and the value used when none is given (none: one must be).</summary>
	public sealed record MidiParameter(string Name, int? Default);

	/// <summary>
	/// A band's shortcut for MIDI commands (Band → MIDI expressions), used in lyrics as
	/// "$NAME.PARAM1.PARAM2": its <see cref="Output"/> is commands (see <see cref="Midi"/>) with
	/// "{name}" for each parameter, e.g. "MSB63 LSB{bank} PC{program}@1". The output may use other
	/// expressions too.
	/// </summary>
	public sealed record MidiExpression(string Name, string Description, IReadOnlyList<MidiParameter> Parameters, string Output)
	{
		/// <summary>The parameters as typed in the editor: "bank=0, program".</summary>
		public string ParametersText => string.Join(", ", Parameters.Select(p => p.Default is { } value ? $"{p.Name}={value}" : p.Name));

		/// <summary>How it's used in lyrics: "$MODX.bank.program".</summary>
		public string Usage => "$" + string.Join('.', new[] { Name }.Concat(Parameters.Select(p => p.Name)));
	}

	/// <summary>
	/// A band's MIDI commands for every song (Band → MIDI), each in the commands of a MIDI tag (see
	/// <see cref="Midi"/>), e.g. "$LIGHT.1, PC0@10": sent when a song is selected (before the song's
	/// own tags), when it starts playing and when it stops. Empty: nothing is sent then.
	/// </summary>
	public sealed record MidiSongCommands(string Select, string Start, string Stop)
	{
		public static readonly MidiSongCommands None = new("", "", "");

		/// <summary>Whether nothing is sent at all.</summary>
		public bool IsEmpty => string.IsNullOrWhiteSpace(Select) && string.IsNullOrWhiteSpace(Start) && string.IsNullOrWhiteSpace(Stop);
	}

	/// <summary>
	/// The MIDI tags in a song's lyrics, as SongbookPro writes them: "[midi: ...]" with commands
	/// separated by commas or spaces, e.g. "[midi:$MODX.0.20, PC1@2]". The tags at the start of the
	/// song (before its first line of lyrics or heading, or leading that line) are sent when the song
	/// is opened (see <see cref="SendSongStart"/>), so the instruments, effects and lights are set for it.
	/// <para>
	/// Commands, each with an optional channel "@1" to "@16" (channel 1 if none):
	/// "PC<i>n</i>" program change (0–127), "CC<i>n</i>:<i>v</i>" or "CC<i>n</i>.<i>v</i>" control change,
	/// "MSB<i>v</i>" and "LSB<i>v</i>" bank select (CC 0 and CC 32), and "$NAME.PARAM1.PARAM2": one
	/// of the band's <see cref="MidiExpression"/>s. Its values are whole numbers from 0 to 127, by
	/// position; one left out (or empty, "$MODX..20") is the parameter's default. A "$NAME" that
	/// isn't one of the band's (e.g. a device name, as SongbookPro writes them) is skipped, as is an
	/// expression that can't be sent (a value missing or wrong) and anything else not known. Every
	/// message goes to every MIDI output.
	/// </para>
	/// </summary>
	public static partial class Midi
	{
		/// <summary>How deep expressions may use other expressions; deeper is taken to be a loop.</summary>
		public const int MaxDepth = 4;

		/// <summary>Sends the MIDI tags at the start of the lyrics, if there are any and MIDI can be sent here.</summary>
		public static void SendSongStart(string? lyrics, IReadOnlyList<MidiExpression>? expressions = null)
		{
			if (App.Midi is { } output && SongStart(lyrics, expressions) is { Count: > 0 } messages)
			{
				output.Send(messages);
			}
		}

		/// <summary>The Yamaha keyboards the transpose can be sent to (see <see cref="YamahaTranspose"/>).</summary>
		public static readonly IReadOnlyList<string> YamahaModels = ["MODX", "YC", "CK"];

		/// <summary>
		/// The keyboards chosen in Settings to get the member's transpose (see <see cref="SettingKeys.SendYamahaTranspose"/>):
		/// some of <see cref="YamahaModels"/>, none when it's off. This device only.
		/// </summary>
		public static IReadOnlyList<string> YamahaTransposeModels
		{
			get => App.Settings.Get(SettingKeys.SendYamahaTranspose) switch
			{
				null or "" => [],
				// Before the models could be chosen, "true" was all of them.
				"true" => YamahaModels,
				var models => YamahaModels.Where(model => models.Split(',').Contains(model, StringComparer.OrdinalIgnoreCase)).ToList(),
			};
			set
			{
				App.Settings.Set(SettingKeys.SendYamahaTranspose, value.Count == 0 ? null : string.Join(",", YamahaModels.Where(value.Contains)));
				// Kept with the user: sent to the server too.
				UserSettings.ChangedHere();
			}
		}

		/// <summary>Sends <see cref="YamahaTranspose"/> to the chosen keyboards (or <paramref name="models"/>), if MIDI can be sent here.</summary>
		public static void SendYamahaTranspose(int semitones, IReadOnlyList<string>? models = null)
		{
			models ??= YamahaTransposeModels;
			if (models.Count > 0)
			{
				App.Midi?.Send(YamahaTranspose(semitones, models));
			}
		}

		/// <summary>
		/// The System Exclusive messages that set the keyboard transpose (in semitones, −12 to +12; 0 for
		/// none) of a Yamaha MODX, YC (YC61/73/88) and CK (CK61/76/88): one per model, each a Parameter
		/// Change "F0 43 1n 7F 1C id ah am al dd F7" to device number 1 (n = 0; received by a keyboard set
		/// to that device number or to All, the default), its data 40 (hex) for 0. Each keyboard takes the
		/// one with its own model ID and ignores the others, so those of the chosen models go to every output.
		/// <list type="bullet">
		/// <item>YC: model ID 09, System Common "Keyboard Transpose" at 20 00 07, data 34–4C (−12–+12):
		/// from the YC88/YC73/YC61 Supplementary Manual (OS V2.00), MIDI Parameter Change Table.</item>
		/// <item>CK: model ID 0B, System Common "Keyboard Transpose" at 20 00 07, data 34–4C (−12–+12):
		/// from the CK88/CK76/CK61 Owner's Manual (MIDI data version 2.0), MIDI Parameter Change Table.</item>
		/// <item>MODX: model ID 07, System "Keyboard Transpose" at 00 00 07, data 40 for 0: from the MODX
		/// Data List as quoted on the YamahaSynth forum (not checked against the Data List itself).</item>
		/// </list>
		/// </summary>
		/// <param name="models">The keyboards to send to, of <see cref="YamahaModels"/>.</param>
		public static IReadOnlyList<byte[]> YamahaTranspose(int semitones, IReadOnlyList<string> models)
		{
			var data = (byte)(0x40 + Math.Clamp(semitones, -12, 12));
			var messages = new List<byte[]>();
			if (models.Contains("MODX"))
			{
				messages.Add(Yamaha(0x07, 0x00, 0x00, 0x07, data));
			}

			if (models.Contains("YC"))
			{
				messages.Add(Yamaha(0x09, 0x20, 0x00, 0x07, data));
			}

			if (models.Contains("CK"))
			{
				messages.Add(Yamaha(0x0B, 0x20, 0x00, 0x07, data));
			}

			return messages;

			// A Parameter Change of the group 7F 1C (MODX, YC, CK) to device number 1.
			static byte[] Yamaha(byte modelId, byte high, byte mid, byte low, byte value)
				=> [0xF0, 0x43, 0x10, 0x7F, 0x1C, modelId, high, mid, low, value, 0xF7];
		}

		/// <summary>Whether the lyrics have MIDI tags at their start (see <see cref="SongStart"/>).</summary>
		public static bool HasSongStart(string? lyrics) => StartTags(lyrics).Any();

		/// <summary>
		/// The messages of the MIDI tags at the start of the lyrics: on the lines before the first line
		/// with lyrics, a heading or a chord (lines of tags only, directives and empty lines), and at the
		/// start of that line.
		/// </summary>
		public static IReadOnlyList<byte[]> SongStart(string? lyrics, IReadOnlyList<MidiExpression>? expressions = null)
		{
			var messages = new List<byte[]>();
			foreach (var commands in StartTags(lyrics))
			{
				messages.AddRange(Parse(commands, expressions));
			}

			return messages;
		}

		/// <summary>
		/// What in the lyrics' MIDI tags can't be sent, in plain words (for the lyrics editor): unknown
		/// commands, expressions that aren't the band's or lack a value, and tags after the start of the
		/// song (only those at the start are sent).
		/// </summary>
		public static IReadOnlyList<string> Problems(string? lyrics, IReadOnlyList<MidiExpression>? expressions)
		{
			var problems = new List<string>();
			if (string.IsNullOrEmpty(lyrics))
			{
				return problems;
			}

			var atStart = StartTags(lyrics).Count();
			var index = 0;
			foreach (Match tag in AnyMidiTag().Matches(lyrics))
			{
				if (index++ >= atStart)
				{
					problems.Add($"\"[midi:{tag.Groups["midi"].Value.Trim()}]\" isn't at the start of the song, so it isn't sent.");
					continue;
				}

				Parse(tag.Groups["midi"].Value, expressions, problems);
			}

			return problems.Distinct().ToList();
		}

		/// <summary>The messages of one tag's commands (the text after "midi:"); what can't be sent goes to <paramref name="problems"/>.</summary>
		public static IReadOnlyList<byte[]> Parse(string commands, IReadOnlyList<MidiExpression>? expressions = null, ICollection<string>? problems = null)
		{
			var messages = new List<byte[]>();
			// "[midi:[midi:$MODX, PC20@1]" is read as one tag: a repeated "midi:" is skipped.
			AddCommands(messages, MidiPrefix().Replace(commands, " "), expressions ?? [], problems, depth: 0);
			return messages;
		}

		/// <summary>
		/// Whether the expression is fine to keep (with <paramref name="others"/>, the band's other
		/// expressions): its name, parameters and output. Null if it is, else why not.
		/// </summary>
		public static string? Check(MidiExpression expression, IReadOnlyList<MidiExpression> others)
		{
			if (!NamePattern().IsMatch(expression.Name))
			{
				return Localization.L.T("The name is letters, digits and _, not starting with a digit (e.g. MODX).");
			}

			if (others.FirstOrDefault(other => string.Equals(other.Name, expression.Name, StringComparison.OrdinalIgnoreCase)) is { } same)
			{
				return Localization.L.T("There's already an expression called {0}.", same.Name);
			}

			if (string.IsNullOrWhiteSpace(expression.Output))
			{
				return Localization.L.T("The output is needed: the MIDI commands to send, e.g. PC{{program}}@1.");
			}

			foreach (Match placeholder in Placeholder().Matches(expression.Output))
			{
				var name = placeholder.Groups[1].Value;
				if (!expression.Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
				{
					return $"{{{name}}} in the output isn't one of the parameters.";
				}
			}

			// Tried with each parameter's default (or 0), as it would be sent.
			var problems = new List<string>();
			var all = others.Append(expression).ToList();
			var messages = Parse(Example(expression), all, problems);
			if (problems.Count > 0)
			{
				return problems[0];
			}

			return messages.Count == 0 ? Localization.L.T("The output sends nothing.") : null;
		}

		/// <summary>Parameters as typed in the editor, "bank=0, program"; null with the reason if they aren't right.</summary>
		public static IReadOnlyList<MidiParameter>? ParseParameters(string? text, out string? problem)
		{
			problem = null;
			var parameters = new List<MidiParameter>();
			foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var pieces = part.Split('=', 2, StringSplitOptions.TrimEntries);
				var name = pieces[0];
				if (!NamePattern().IsMatch(name))
				{
					problem = $"\"{name}\" isn't a parameter name: letters, digits and _, not starting with a digit.";
					return null;
				}

				if (parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
				{
					problem = Localization.L.T("There are two parameters called {0}.", name);
					return null;
				}

				int? defaultValue = null;
				if (pieces.Length == 2)
				{
					if (!TryValue(pieces[1], out var value))
					{
						problem = Localization.L.T("The default of {0} must be a whole number from 0 to 127.", name);
						return null;
					}

					defaultValue = value;
				}

				parameters.Add(new MidiParameter(name, defaultValue));
			}

			return parameters;
		}

		/// <summary>A message in words, e.g. "Program 20, channel 1" or "CC 7 = 100, channel 2".</summary>
		public static string Describe(byte[] message)
		{
			if (message.Length < 2)
			{
				return "?";
			}

			var channel = (message[0] & 0x0F) + 1;
			return (message[0] & 0xF0) switch
			{
				0xC0 => $"Program {message[1]}, channel {channel}",
				0xB0 when message.Length >= 3 && message[1] == 0 => $"Bank MSB {message[2]}, channel {channel}",
				0xB0 when message.Length >= 3 && message[1] == 32 => $"Bank LSB {message[2]}, channel {channel}",
				0xB0 when message.Length >= 3 => $"CC {message[1]} = {message[2]}, channel {channel}",
				_ => string.Join(' ', message.Select(b => b.ToString("X2"))),
			};
		}

		/// <summary>An example of using the expression, with each parameter's default (or 0): "$MODX.0.0".</summary>
		public static string Example(MidiExpression expression)
			=> "$" + string.Join('.', new[] { expression.Name }.Concat(expression.Parameters.Select(p => (p.Default ?? 0).ToString(CultureInfo.InvariantCulture))));

		/// <summary>A band's expressions from how they're stored (a JSON array); what can't be read is left out.</summary>
		public static IReadOnlyList<MidiExpression> ExpressionsFromJson(string? json)
		{
			var expressions = new List<MidiExpression>();
			if (string.IsNullOrWhiteSpace(json))
			{
				return expressions;
			}

			JsonNode? root;
			try
			{
				root = JsonNode.Parse(json);
			}
			catch (System.Text.Json.JsonException)
			{
				return expressions;
			}

			if (root is not JsonArray array)
			{
				return expressions;
			}

			foreach (var item in array.OfType<JsonObject>())
			{
				var name = Text(item["name"]);
				var output = Text(item["output"]);
				if (string.IsNullOrEmpty(name) || output == null)
				{
					continue;
				}

				var parameters = new List<MidiParameter>();
				if (item["parameters"] is JsonArray list)
				{
					foreach (var parameter in list.OfType<JsonObject>())
					{
						if (Text(parameter["name"]) is { Length: > 0 } parameterName)
						{
							var defaultValue = parameter["default"] is JsonValue value && value.TryGetValue<int>(out var number) && number is >= 0 and <= 127 ? number : (int?)null;
							parameters.Add(new MidiParameter(parameterName, defaultValue));
						}
					}
				}

				expressions.Add(new MidiExpression(name, Text(item["description"]) ?? "", parameters, output));
			}

			return expressions;

			static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
		}

		/// <summary>A band's commands for every song from how they're stored (a JSON object of select, start and stop); none if they can't be read.</summary>
		public static MidiSongCommands SongCommandsFromJson(string? json)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				return MidiSongCommands.None;
			}

			try
			{
				if (JsonNode.Parse(json) is not JsonObject item)
				{
					return MidiSongCommands.None;
				}

				return new MidiSongCommands(Text(item["select"]), Text(item["start"]), Text(item["stop"]));
			}
			catch (System.Text.Json.JsonException)
			{
				return MidiSongCommands.None;
			}

			static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
		}

		/// <summary>A band's commands for every song as they're stored: a JSON object; null if there are none.</summary>
		public static string? SongCommandsToJson(MidiSongCommands commands)
		{
			if (commands.IsEmpty)
			{
				return null;
			}

			var item = new JsonObject();
			foreach (var (key, value) in new[] { ("select", commands.Select), ("start", commands.Start), ("stop", commands.Stop) })
			{
				if (!string.IsNullOrWhiteSpace(value))
				{
					item[key] = value.Trim();
				}
			}

			return item.ToJsonString();
		}

		/// <summary>A band's expressions as they're stored: a JSON array.</summary>
		public static string ExpressionsToJson(IEnumerable<MidiExpression> expressions)
		{
			var array = new JsonArray();
			foreach (var expression in expressions)
			{
				var parameters = new JsonArray();
				foreach (var parameter in expression.Parameters)
				{
					var item = new JsonObject { ["name"] = parameter.Name };
					if (parameter.Default is { } value)
					{
						item["default"] = value;
					}

					parameters.Add(item);
				}

				var node = new JsonObject { ["name"] = expression.Name };
				if (!string.IsNullOrWhiteSpace(expression.Description))
				{
					node["description"] = expression.Description;
				}

				node["parameters"] = parameters;
				node["output"] = expression.Output;
				array.Add(node);
			}

			return array.ToJsonString();
		}

		/// <summary>The commands of the MIDI tags at the start of the lyrics (see <see cref="SongStart"/>).</summary>
		private static IEnumerable<string> StartTags(string? lyrics)
		{
			if (string.IsNullOrEmpty(lyrics))
			{
				yield break;
			}

			foreach (var rawLine in lyrics.Split('\n'))
			{
				var line = rawLine.Trim();
				if (line.Length == 0 || DirectiveLine().IsMatch(line))
				{
					continue;
				}

				// The hidden tags leading the line (MIDI and time tags); the line goes on after them.
				var at = 0;
				while (LeadingHiddenTag().Match(line, at) is { Success: true } tag && tag.Index == at)
				{
					if (tag.Groups["midi"].Success)
					{
						yield return tag.Groups["midi"].Value;
					}

					at = tag.Index + tag.Length;
				}

				if (at < line.Length)
				{
					yield break; // The song itself starts here.
				}
			}
		}

		private static void AddCommands(List<byte[]> messages, string commands, IReadOnlyList<MidiExpression> expressions, ICollection<string>? problems, int depth)
		{
			foreach (var token in Separators().Split(commands))
			{
				if (token.Length == 0)
				{
					continue;
				}

				if (token[0] == '$')
				{
					AddExpression(messages, token, expressions, problems, depth);
				}
				else if (ParseCommand(token) is { } message)
				{
					messages.Add(message);
				}
				else
				{
					problems?.Add($"\"{token}\" isn't a MIDI command.");
				}
			}
		}

		/// <summary>"$NAME.PARAM1.PARAM2": the expression's output with the values put in, all of it or (if anything's wrong) nothing.</summary>
		private static void AddExpression(List<byte[]> messages, string token, IReadOnlyList<MidiExpression> expressions, ICollection<string>? problems, int depth)
		{
			var parts = token[1..].Split('.');
			var name = parts[0];
			if (expressions.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } expression)
			{
				problems?.Add($"${name} isn't one of the band's MIDI expressions, so nothing is sent for it.");
				return;
			}

			if (depth >= MaxDepth)
			{
				problems?.Add($"${expression.Name} uses expressions more than {MaxDepth} deep (perhaps in a loop), so it isn't sent.");
				return;
			}

			var values = parts[1..];
			if (values.Length > expression.Parameters.Count)
			{
				problems?.Add($"\"{token}\" has {values.Length} values, but ${expression.Name} takes {expression.Parameters.Count}.");
				return;
			}

			var output = expression.Output;
			for (var i = 0; i < expression.Parameters.Count; i++)
			{
				var parameter = expression.Parameters[i];
				int value;
				if (i < values.Length && values[i].Length > 0)
				{
					if (!TryValue(values[i], out value))
					{
						problems?.Add($"\"{token}\": {parameter.Name} must be a whole number from 0 to 127, not \"{values[i]}\".");
						return;
					}
				}
				else if (parameter.Default is { } defaultValue)
				{
					value = defaultValue;
				}
				else
				{
					problems?.Add($"\"{token}\" needs a value for {parameter.Name} ({expression.Usage}).");
					return;
				}

				output = Regex.Replace(output, @"\{" + Regex.Escape(parameter.Name) + @"\}", value.ToString(CultureInfo.InvariantCulture), RegexOptions.IgnoreCase);
			}

			var inner = new List<byte[]>();
			var innerProblems = new List<string>();
			AddCommands(inner, output, expressions, innerProblems, depth + 1);
			if (innerProblems.Count > 0)
			{
				foreach (var problem in innerProblems)
				{
					// Named once, by the expression used in the lyrics.
					problems?.Add(depth == 0 ? $"${expression.Name}: {problem}" : problem);
				}

				return;
			}

			messages.AddRange(inner);
		}

		/// <summary>One plain command as a message; null if it isn't one.</summary>
		private static byte[]? ParseCommand(string token)
		{
			if (Command().Match(token) is not { Success: true } command)
			{
				return null;
			}

			var channel = command.Groups["channel"].Success && int.TryParse(command.Groups["channel"].Value, out var given) ? given : 1;
			var hasValue = command.Groups["value"].Success;
			var value = 0;
			if (channel is < 1 or > 16
			    || !TryValue(command.Groups["number"].Value, out var number)
			    || (hasValue && !TryValue(command.Groups["value"].Value, out value)))
			{
				return null;
			}

			var status = (byte)(channel - 1);
			return command.Groups["kind"].Value.ToUpperInvariant() switch
			{
				"PC" when !hasValue => [(byte)(0xC0 | status), (byte)number],
				"CC" when hasValue => [(byte)(0xB0 | status), (byte)number, (byte)value],
				"MSB" when !hasValue => [(byte)(0xB0 | status), 0, (byte)number],
				"LSB" when !hasValue => [(byte)(0xB0 | status), 32, (byte)number],
				_ => null,
			};
		}

		/// <summary>A MIDI data value: a whole number from 0 to 127.</summary>
		private static bool TryValue(string text, out int value)
			=> int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 127;

		/// <summary>A command: kind, number, perhaps a value (after ':' or '.') and a channel (after '@').</summary>
		[GeneratedRegex(@"^(?<kind>PC|CC|MSB|LSB)(?<number>\d+)(?:[:.](?<value>\d+))?(?:@(?<channel>\d+))?$", RegexOptions.IgnoreCase)]
		private static partial Regex Command();

		[GeneratedRegex(@"[\s,;]+")]
		private static partial Regex Separators();

		[GeneratedRegex(@"\[?\s*midi\s*:", RegexOptions.IgnoreCase)]
		private static partial Regex MidiPrefix();

		/// <summary>An expression's or parameter's name.</summary>
		[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
		private static partial Regex NamePattern();

		/// <summary>"{name}" in an expression's output.</summary>
		[GeneratedRegex(@"\{([^{}]*)\}")]
		private static partial Regex Placeholder();

		/// <summary>
		/// A hidden tag (as <see cref="Setlists.ChordPro"/> hides them): a MIDI tag, with its commands
		/// in "midi", or a time tag; and the spaces after it. A "[" inside belongs to the tag.
		/// </summary>
		[GeneratedRegex(@"\[\s*(?:[Mm][Ii][Dd][Ii]\s*:(?<midi>[^\]\n]*)|\+?\d[^\]\n]*)\]\s*")]
		private static partial Regex LeadingHiddenTag();

		/// <summary>Any MIDI tag in the lyrics, with its commands in "midi".</summary>
		[GeneratedRegex(@"\[\s*[Mm][Ii][Dd][Ii]\s*:(?<midi>[^\]\n]*)\]")]
		private static partial Regex AnyMidiTag();

		/// <summary>A directive on a line of its own: {name} or {name: value}.</summary>
		[GeneratedRegex(@"^\{[A-Za-z_]+(?:[:\s].*)?\}$")]
		private static partial Regex DirectiveLine();
	}
}
