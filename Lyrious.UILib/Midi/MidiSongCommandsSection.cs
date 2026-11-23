using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A band's MIDI commands for every song, in the band settings on the Settings page (see
	/// <see cref="MidiSongCommands"/>): sent when a song is selected (before its own MIDI tags), starts
	/// and stops. They're saved with the band, the same for all its members. An admin types them, in
	/// the commands of a MIDI tag (the band's expressions too); under each, as it's typed, what it
	/// sends or what's wrong. Save only when something changed and nothing is wrong. The others see them.
	/// </summary>
	public sealed class MidiSongCommandsSection : UserControl
	{
		/// <param name="expressions">The band's MIDI expressions, which the commands may use.</param>
		/// <param name="canEdit">Whether the member is an admin of the band (only they change its commands).</param>
		/// <param name="save">Saves the changed commands with the band.</param>
		public MidiSongCommandsSection(MidiSongCommands commands, IReadOnlyList<MidiExpression> expressions, bool canEdit, Func<MidiSongCommands, Task> save)
		{
			var panel = new StackPanel
			{
				Spacing = 8,
				Children =
				{
					// Its explanation as the heading's tooltip.
					SettingsView.SettingName(
						L.T("MIDI for every song"),
						L.T("MIDI sent for every song to every MIDI output: when it's selected (before the song's own MIDI tags), when it starts playing and when it stops. ")
						+ L.T("Written as in a MIDI tag, e.g. $LIGHT.1, PC0@10.")
						+ (canEdit ? "" : L.T(" Only the band's admins change them."))),
				},
			};
			Content = panel;

			if (!canEdit)
			{
				panel.Children.Add(ReadOnly(L.T("MIDI when a song is selected"), commands.Select));
				panel.Children.Add(ReadOnly(L.T("MIDI when a song starts"), commands.Start));
				panel.Children.Add(ReadOnly(L.T("MIDI when a song stops"), commands.Stop));
				return;
			}

			var select = Box(commands.Select);
			var start = Box(commands.Start);
			var stop = Box(commands.Stop);
			var notes = new[] { Note(), Note(), Note() };
			var saveButton = new Button { Content = L.T("Save MIDI settings"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
			var saved = commands;
			var saving = false;
			var saveProblem = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
			saveProblem.Bind(TextBlock.ForegroundProperty, saveProblem.GetResourceObservable(LyriousTheme.ErrorBrush));

			// Each line's messages (or what's wrong); Save once something changed and nothing is wrong.
			void Check()
			{
				var ok = true;
				var boxes = new[] { select, start, stop };
				for (var i = 0; i < boxes.Length; i++)
				{
					var text = boxes[i].Text?.Trim() ?? "";
					var problems = new List<string>();
					var messages = Midi.Parse(text, expressions, problems);
					notes[i].Text = text.Length == 0
						? L.T("Nothing is sent.")
						: problems.Count > 0
							? string.Join(" ", problems)
							: L.T("Sends: {0}.", string.Join("; ", messages.Select(Midi.Describe)));
					notes[i].Opacity = problems.Count > 0 ? 1 : 0.7;
					if (problems.Count > 0)
					{
						notes[i].Bind(TextBlock.ForegroundProperty, notes[i].GetResourceObservable(LyriousTheme.WarningBrush));
					}
					else
					{
						notes[i].ClearValue(TextBlock.ForegroundProperty);
					}

					ok &= problems.Count == 0;
				}

				saveButton.IsEnabled = ok && !saving && Current() != saved;
			}

			MidiSongCommands Current() => new(select.Text?.Trim() ?? "", start.Text?.Trim() ?? "", stop.Text?.Trim() ?? "");

			foreach (var box in new[] { select, start, stop })
			{
				box.TextChanged += (_, _) => Check();
			}

			saveButton.Click += async (_, _) =>
			{
				var changed = Current();
				saving = true;
				saveProblem.IsVisible = false;
				Check();
				try
				{
					await save(changed);
					saved = changed;
				}
				catch (Exception ex)
				{
					saveProblem.Text = L.T("Couldn't save the MIDI settings. {0}", ex.Message);
					saveProblem.IsVisible = true;
				}
				finally
				{
					saving = false;
					Check();
				}
			};

			panel.Children.Add(Field(L.T("MIDI when a song is selected"), select, notes[0]));
			panel.Children.Add(Field(L.T("MIDI when a song starts"), start, notes[1]));
			panel.Children.Add(Field(L.T("MIDI when a song stops"), stop, notes[2]));
			panel.Children.Add(saveButton);
			panel.Children.Add(saveProblem);
			Check();
		}

		private static TextBox Box(string text) => new() { Text = text, PlaceholderText = "e.g. $LIGHT.1, PC0@10", FontFamily = FontFamily.Parse("monospace") };

		private static TextBlock Note() => new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };

		private static Control Field(string label, TextBox box, TextBlock note) => new StackPanel
		{
			Spacing = 4,
			Children = { new TextBlock { Text = label }, box, note },
		};

		private static Control ReadOnly(string label, string text) => new StackPanel
		{
			Spacing = 2,
			Children =
			{
				new TextBlock { Text = label },
				string.IsNullOrWhiteSpace(text)
					? new TextBlock { Text = L.T("Nothing is sent."), Opacity = 0.6 }
					: new SelectableTextBlock { Text = text, FontFamily = FontFamily.Parse("monospace"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 },
			},
		};
	}
}
