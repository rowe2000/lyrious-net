using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lyrious.UILib.Localization;
using Lyrious.UILib.Setlists;

namespace Lyrious.UILib
{
	/// <summary>
	/// The MIDI page (from the play toolbar's MIDI button): the user sounds of the member's Yamaha keyboard
	/// (a YC, CK or MODX, see <see cref="YamahaKeyboard"/>), read from it over MIDI (and kept on this
	/// device), to choose the one the song is played with. The choice is kept in the member's own settings
	/// for the song (only they see it) and the keyboard switches to it at once, and again whenever the song
	/// is chosen. Without a song open, a sound is only played.
	/// </summary>
	public sealed class MidiSoundsView : UserControl
	{
		private const string KeyboardKey = "midi-keyboard";
		private const string ChannelKey = "midi-keyboard-channel";

		private readonly LyricScrollView? lyrics;
		private readonly ListBox list = new() { MinHeight = 200 };
		private readonly TextBox filter = new() { PlaceholderText = L.T("Find a sound") };
		private readonly TextBlock current = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
		private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
		private readonly NumericUpDown channel = new() { Minimum = 1, Maximum = 16, Increment = 1, FormatString = "0", Width = 120 };
		private readonly Button read = new();
		private YamahaKeyboard keyboard = YamahaKeyboard.ById(App.Settings.Get(KeyboardKey));
		private IReadOnlyList<KeyboardSound> sounds = [];
		private bool showing;
		private CancellationTokenSource? reading;

		/// <param name="lyrics">The song open (its sound is chosen here); null when none is.</param>
		/// <param name="songName">The open song's name.</param>
		public MidiSoundsView(LyricScrollView? lyrics, string? songName)
		{
			this.lyrics = lyrics;
			sounds = keyboard.Kept;
			channel.Value = int.TryParse(App.Settings.Get(ChannelKey), NumberStyles.None, CultureInfo.InvariantCulture, out var kept) && kept is >= 1 and <= 16 ? kept : 1;
			channel.ValueChanged += (_, _) => App.Settings.Set(ChannelKey, channel.Value is { } value && value != 1 ? ((int)value).ToString(CultureInfo.InvariantCulture) : null);

			// Which keyboard (remembered on this device): its sounds read last show at once.
			var model = new SettingComboBox { MinWidth = 180 };
			foreach (var choice in YamahaKeyboard.All)
			{
				model.Items.Add(new ComboBoxItem { Content = choice.Name, Tag = choice });
			}

			model.SelectedIndex = YamahaKeyboard.All.ToList().IndexOf(keyboard);
			model.SelectionChanged += (_, _) =>
			{
				if (model.SelectedItem is ComboBoxItem { Tag: YamahaKeyboard chosen } && chosen != keyboard)
				{
					reading?.Cancel();
					keyboard = chosen;
					App.Settings.Set(KeyboardKey, chosen == YamahaKeyboard.Yc ? null : chosen.Id);
					sounds = keyboard.Kept;
					status.Text = "";
					ShowKeyboard();
				}
			};

			read.Classes.Add("accent");
			var cancelRead = new Button { Content = L.T("Stop"), IsVisible = false };
			var none = new Button { Content = L.T("No sound"), IsEnabled = lyrics != null };
			read.IsEnabled = App.Midi is IMidiRequester;
			read.Click += async (_, _) =>
			{
				if (App.Midi is not IMidiRequester midi)
				{
					return;
				}

				var from = keyboard;
				reading = new CancellationTokenSource();
				read.IsEnabled = false;
				model.IsEnabled = false;
				cancelRead.IsVisible = true;
				var total = from.Banks * from.PerBank;
				status.Text = L.T("Asking the keyboard…");
				try
				{
					var progress = new Progress<int>(done => status.Text = L.T("Reading the sounds… {0} of {1}", done, total));
					var found = await from.ReadSoundsAsync(midi, progress, reading.Token);
					if (found == null)
					{
						status.Text = L.T("The {0} didn't answer. Check that it's connected by MIDI or USB, with MIDI or USB In/Out on, and its device number 1.", from.Name);
					}
					else
					{
						sounds = found;
						status.Text = L.Plural(found.Count, "{0} sound read.", "{0} sounds read.");
						ShowSounds();
					}
				}
				catch (OperationCanceledException)
				{
					status.Text = L.T("Stopped.");
				}
				finally
				{
					read.IsEnabled = true;
					model.IsEnabled = true;
					cancelRead.IsVisible = false;
					reading = null;
				}
			};
			cancelRead.Click += (_, _) => reading?.Cancel();
			DetachedFromVisualTree += (_, _) => reading?.Cancel();

			none.Click += (_, _) =>
			{
				lyrics?.SetMidiSound("");
				list.SelectedItem = null;
				ShowCurrent();
			};
			filter.TextChanged += (_, _) => ShowSounds();
			list.SelectionChanged += (_, _) =>
			{
				if (!showing && list.SelectedItem is ListBoxItem { Tag: KeyboardSound sound })
				{
					Choose(sound);
				}
			};

			var intro = songName is { Length: > 0 } && lyrics != null
				? L.T("The sound of your keyboard for {0}: chosen here, kept for you only, and sent whenever the song is chosen.", songName)
				: L.T("Open a song to keep a sound for it; here a sound is only played.");

			var tools = new WrapPanel { Orientation = Orientation.Horizontal, Children = { read, cancelRead, none } };
			foreach (var child in tools.Children.OfType<Control>())
			{
				child.Margin = new Thickness(0, 0, 8, 8);
			}

			var form = new StackPanel
			{
				Spacing = 12,
				Margin = new Thickness(0, 0, 0, 16),
				Children =
				{
					new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
					current,
					SettingsView.Setting(L.T("Keyboard"), L.T("Your Yamaha keyboard. On this device."), model),
					SettingsView.Setting(L.T("MIDI channel"), L.T("The channel the keyboard receives on (1 unless changed on it). On this device."), channel),
					tools,
					status,
					filter,
					list,
				},
			};
			Content = PageLayout.Page(
				L.T("MIDI"),
				new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
				closable: true,
				icon: AppIcons.Midi);

			ShowKeyboard();
		}

		/// <summary>The keyboard chosen: its read button, the song's sound on it, and its sounds read last.</summary>
		private void ShowKeyboard()
		{
			read.Content = L.T("Read the sounds from the {0}", keyboard.Id);
			if (App.Midi is not IMidiRequester)
			{
				status.Text = L.T("The sounds can't be read from a keyboard here.");
			}

			ShowCurrent();
			ShowSounds();
		}

		/// <summary>The sound chosen for the song now (or none), and where it is on the keyboard.</summary>
		private void ShowCurrent()
		{
			current.IsVisible = lyrics != null;
			if (lyrics == null)
			{
				return;
			}

			var sound = MidiSound.Parse(lyrics.MySetting.MidiSound);
			current.Text = sound == null
				? L.T("No sound chosen for this song.")
				: keyboard.Place(sound) is { } place
					? L.T("Chosen: {0} ({1})", sound.Name, place)
					: L.T("Chosen: {0}", sound.Name);
		}

		/// <summary>The sounds read (those whose name has the text in the search box), the song's selected.</summary>
		private void ShowSounds()
		{
			showing = true;
			try
			{
				var text = (filter.Text ?? "").Trim();
				var chosen = MidiSound.Parse(lyrics?.MySetting.MidiSound);
				list.Items.Clear();
				foreach (var sound in sounds.Where(s => text.Length == 0 || s.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)))
				{
					var item = new ListBoxItem
					{
						Tag = sound,
						Content = $"{keyboard.Place(sound)}   {(sound.Name.Length > 0 ? sound.Name : L.T("(no name)"))}",
					};
					list.Items.Add(item);
					if (chosen != null && keyboard.Selects(chosen, sound))
					{
						list.SelectedItem = item;
					}
				}

				list.IsVisible = list.Items.Count > 0;
				filter.IsVisible = sounds.Count > 0;
				if (sounds.Count == 0 && App.Midi is IMidiRequester && string.IsNullOrEmpty(status.Text))
				{
					status.Text = L.T("Read the sounds from the keyboard to choose among them.");
				}
			}
			finally
			{
				showing = false;
			}
		}

		/// <summary>Plays the sound on the keyboard now and keeps it for the song (if one is open).</summary>
		private void Choose(KeyboardSound sound)
		{
			var midi = keyboard.Midi(sound, (int)(channel.Value ?? 1));
			MidiModule.Output?.Send(midi.Messages());
			lyrics?.SetMidiSound(midi.Format());
			ShowCurrent();
		}
	}
}
