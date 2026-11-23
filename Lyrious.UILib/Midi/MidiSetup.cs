using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Lyrious.UILib.Localization;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib
{
	/// <summary>
	/// The MIDI module's settings (see <see cref="MidiModule"/>), on the Settings page: whether MIDI is
	/// on, and while it is, the member's
	/// MIDI settings (kept with the user; the band's until they save their own): the expressions used in
	/// songs' MIDI tags, the MIDI sent for every song, and (where MIDI can be sent) which Yamaha
	/// keyboards get the member's transpose.
	/// </summary>
	internal static class MidiSetup
	{
		/// <summary>The "Use MIDI" switch, and the MIDI settings (shown while MIDI is on, here or on another device).</summary>
		/// <param name="bandMidiExpressions">The band's MIDI expressions as bands kept them, which count until the member saves their own.</param>
		/// <param name="bandMidiCommands">The band's MIDI commands as bands kept them, which count until the member saves their own.</param>
		public static (Control UseMidi, Control Settings) Create(string? bandMidiExpressions, string? bandMidiCommands)
		{
			var midiOn = new ToggleSwitch
			{
				IsChecked = MidiModule.Enabled,
				OnContent = L.T("On"),
				OffContent = L.T("Off"),
			};
			midiOn.IsCheckedChanged += (_, _) => MidiModule.Enabled = midiOn.IsChecked == true;
			var useMidi = SettingsView.Setting(
				L.T("Use MIDI"),
				L.T("Sends MIDI from the app: the MIDI tags in songs' lyrics and the MIDI commands for every song, to instruments, effects and lights on the MIDI outputs. Off, no MIDI is sent and its settings are hidden. Yours on all your devices."),
				midiOn);

			var expressions = MidiSettings.Expressions(bandMidiExpressions);
			var settings = new StackPanel
			{
				Spacing = 16,
				IsVisible = MidiModule.Enabled,
				Children =
				{
					new MidiExpressionsSection(expressions, canEdit: true, MidiSettings.SaveExpressions),
					new MidiSongCommandsSection(
						MidiSettings.Commands(bandMidiCommands),
						expressions,
						canEdit: true,
						commands =>
						{
							MidiSettings.SaveCommands(commands);
							return Task.CompletedTask;
						}),
				},
			};
			if (MidiModule.Available)
			{
				settings.Children.Add(SettingsView.Setting(
					L.T("Transpose to Yamaha keyboards"),
					L.T("Sets the keyboard transpose of the chosen keyboards on the MIDI outputs to your transpose or capo for each song (by MIDI System Exclusive). Yours on all your devices."),
					SettingsView.YamahaTransposeChoice()));
			}

			// On or off here or on another device: the settings show or hide at once (while on screen).
			void Show() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				midiOn.IsChecked = MidiModule.Enabled;
				settings.IsVisible = MidiModule.Enabled;
			});
			useMidi.AttachedToVisualTree += (_, _) =>
			{
				MidiModule.EnabledChanged += Show;
				UserSettings.Changed += Show;
				Show();
			};
			useMidi.DetachedFromVisualTree += (_, _) =>
			{
				MidiModule.EnabledChanged -= Show;
				UserSettings.Changed -= Show;
			};
			return (useMidi, settings);
		}
	}
}
