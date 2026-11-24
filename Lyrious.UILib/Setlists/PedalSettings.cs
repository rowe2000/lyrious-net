using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>What a pedal can do on the lyrics page.</summary>
	public enum PedalAction
	{
		PageForward,
		PageBack,
		NextSong,
		PreviousSong,
		PlayStop,
		NudgeForward,
		NudgeBack,
		StartOfSong,
	}

	/// <summary>A learned pedal: the key it sends, and what it does.</summary>
	public sealed record PedalKey(Key Key, PedalAction Action);

	/// <summary>
	/// Pedals (page turners such as AirTurn, which connect as a keyboard): the keys learned from them on
	/// this device, each with what it does on the lyrics page; they take over those keys there (see
	/// HomeView.OnLyricsKey). Kept on this device (<see cref="SettingKeys.Pedals"/>), as a pedal is
	/// paired with a device.
	/// </summary>
	public static class PedalSettings
	{
		/// <summary>The learned pedals, in the order learned.</summary>
		public static IReadOnlyList<PedalKey> Keys
		{
			get => Parse(App.Settings.Get(SettingKeys.Pedals));
			set => App.Settings.Set(SettingKeys.Pedals, value.Count == 0 ? null : string.Join(",", value.Select(pedal => $"{pedal.Key}:{pedal.Action}")));
		}

		/// <summary>What the key does as a learned pedal; null when no pedal sends it.</summary>
		public static PedalAction? ActionFor(Key key) => Keys.FirstOrDefault(pedal => pedal.Key == key)?.Action;

		/// <summary>Learns (or learns again) the pedal that sends the key, doing what fits it at first (see <see cref="DefaultFor"/>).</summary>
		public static void Learn(Key key)
			=> Keys = [.. Keys.Where(pedal => pedal.Key != key), new PedalKey(key, DefaultFor(key))];

		/// <summary>Changes what the pedal that sends the key does.</summary>
		public static void SetAction(Key key, PedalAction action)
			=> Keys = Keys.Select(pedal => pedal.Key == key ? pedal with { Action = action } : pedal).ToList();

		/// <summary>Forgets the pedal that sends the key.</summary>
		public static void Forget(Key key) => Keys = Keys.Where(pedal => pedal.Key != key).ToList();

		/// <summary>
		/// What a newly learned pedal does: a page on for keys that go forward (Page Down, the right and
		/// down arrows), a page back for those that go back, play and stop for Space and Enter.
		/// </summary>
		public static PedalAction DefaultFor(Key key) => key switch
		{
			Key.PageUp or Key.Left or Key.Up or Key.MediaPreviousTrack => PedalAction.PageBack,
			Key.Space or Key.Enter or Key.MediaPlayPause => PedalAction.PlayStop,
			_ => PedalAction.PageForward,
		};

		/// <summary>What the action does, as Settings names it.</summary>
		public static string Name(PedalAction action) => action switch
		{
			PedalAction.PageForward => L.T("A page on"),
			PedalAction.PageBack => L.T("A page back"),
			PedalAction.NextSong => L.T("Next song"),
			PedalAction.PreviousSong => L.T("Previous song"),
			PedalAction.PlayStop => L.T("Play and stop"),
			PedalAction.NudgeForward => L.T("A line on"),
			PedalAction.NudgeBack => L.T("A line back"),
			_ => L.T("Back to the start"),
		};

		/// <summary>A key's name as a pedal sends it: "Page Down", "→", "Space".</summary>
		public static string KeyName(Key key) => key switch
		{
			Key.PageDown => L.T("Page Down"),
			Key.PageUp => L.T("Page Up"),
			Key.Right => "→",
			Key.Left => "←",
			Key.Up => "↑",
			Key.Down => "↓",
			Key.Space => L.T("Space"),
			Key.Enter => L.T("Enter"),
			_ => key.ToString(),
		};

		/// <summary>The learned pedals from their setting: "PageDown:PageForward,PageUp:PageBack"; what can't be read is left out.</summary>
		private static IReadOnlyList<PedalKey> Parse(string? text)
			=> (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(part => part.Split(':'))
				.Where(parts => parts.Length == 2 && Enum.TryParse<Key>(parts[0], out _) && Enum.TryParse<PedalAction>(parts[1], out _))
				.Select(parts => new PedalKey(Enum.Parse<Key>(parts[0]), Enum.Parse<PedalAction>(parts[1])))
				.ToList();
	}
}
