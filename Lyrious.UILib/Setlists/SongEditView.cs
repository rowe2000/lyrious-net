using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Edits a song, or fills in a new one: name, key, tempo, beat and lyrics. Save (✓) and Cancel (✕) are beside the
	/// title, as in the lyrics editor. If saving fails the error is shown and the edits are kept.
	/// </summary>
	public sealed class SongEditView : UserControl
	{
		private const string SaveIcon = "M5,12 L10,17 L19,7";
		private const string CancelIcon = "M6,6 L18,18 M18,6 L6,18";

		// The server's limits for these fields.
		private const int MaxNameLength = 100;
		private const int MaxLyricsLength = 5000;

		private static readonly string[] Keys =
		[
			"C", "C#", "Db", "D", "D#", "Eb", "E", "F", "F#", "Gb", "G", "G#", "Ab", "A", "A#", "Bb", "B",
			"Cm", "C#m", "Dm", "D#m", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "A#m", "Bbm", "Bm",
		];

		private static readonly string[] Beats = ["4/4", "3/4", "2/4", "6/8", "12/8", "5/4", "7/8"];

		private const string NoKey = "(no key)";

		/// <summary>Raised when the user cancels.</summary>
		public event Action? Cancelled;

		/// <summary>Raised with the stored song after saving.</summary>
		public event Action<SongDetails>? Saved;

		/// <param name="save">Stores the edited song and returns it as stored; throws if that fails.</param>
		/// <param name="title">The page title, e.g. "New song" when filling in a song that doesn't exist yet.</param>
		public SongEditView(SongDetails song, Func<SongDetails, Task<SongDetails>> save, string title = "Edit song")
		{
			var name = new TextBox { Text = song.Name, MaxLength = MaxNameLength, PlaceholderText = "Song name" };
			var key = Choice(Keys, song.Key, NoKey);
			var beat = Choice(Beats, song.Beat, null);
			var tempo = new NumericUpDown { Value = (decimal)Math.Clamp(song.Tempo, 20, 300), Minimum = 20, Maximum = 300, Increment = 1, FormatString = "0", MinWidth = 140 };
			var lyrics = new TextBox
			{
				Text = song.Lyrics,
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MaxLength = MaxLyricsLength,
				FontSize = 16,
				MinHeight = 200,
				VerticalContentAlignment = VerticalAlignment.Top,
				PlaceholderText = "Type or paste the lyrics",
			};

			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));

			var saveButton = HomeView.IconButton(SaveIcon, "Save song");
			// Escape cancels too.
			var cancelButton = EscapeKey.MarkAsClose(HomeView.IconButton(CancelIcon, "Cancel editing"), first: true);
			cancelButton.Click += (_, _) => Cancelled?.Invoke();
			saveButton.IsEnabled = !string.IsNullOrWhiteSpace(song.Name);
			name.TextChanged += (_, _) => saveButton.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
			name.AttachedToVisualTree += (_, _) => name.Focus();

			saveButton.Click += async (_, _) =>
			{
				var edited = song with
				{
					Name = name.Text!.Trim(),
					Key = key.SelectedItem as string is { } k && k != NoKey ? k : "",
					Beat = beat.SelectedItem as string ?? song.Beat,
					Tempo = (float)(tempo.Value ?? (decimal)song.Tempo),
					Lyrics = lyrics.Text ?? "",
				};

				saveButton.IsEnabled = cancelButton.IsEnabled = false;
				error.IsVisible = false;
				try
				{
					Saved?.Invoke(await save(edited));
				}
				catch (Exception ex)
				{
					error.Text = $"Couldn't save. {ex.Message}";
					error.IsVisible = true;
					saveButton.IsEnabled = cancelButton.IsEnabled = true;
				}
			};

			var details = new WrapPanel
			{
				Orientation = Orientation.Horizontal,
				Children = { Field("Key", key), Field("Tempo (bpm)", tempo), Field("Beat", beat) },
			};

			var form = new StackPanel
			{
				Spacing = 12,
				Children = { error, Field("Name", name), details, Field("Lyrics", lyrics) },
			};

			Content = PageLayout.Page(
				title,
				new ScrollViewer { Content = form, Padding = new Thickness(0, 0, PageLayout.ScrollBarRoom, 24) }, // Room at the right for the scroll bar, as on every page.
				subtitle: string.IsNullOrWhiteSpace(song.Name) ? null : song.Name,
				titleAction: new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { saveButton, cancelButton } });
		}

		/// <summary>A drop-down of <paramref name="options"/>, with the current value added if it isn't one of them.</summary>
		private static ComboBox Choice(IEnumerable<string> options, string current, string? none)
		{
			var items = new List<string>();
			if (none != null)
			{
				items.Add(none);
			}

			items.AddRange(options);
			if (!string.IsNullOrWhiteSpace(current) && !items.Contains(current))
			{
				items.Add(current);
			}

			return new ComboBox
			{
				ItemsSource = items,
				SelectedItem = string.IsNullOrWhiteSpace(current) ? none ?? items.FirstOrDefault() : current,
				MinWidth = 120,
			};
		}

		private static Control Field(string label, Control input) => new StackPanel
		{
			Spacing = 4,
			Margin = new Thickness(0, 0, 16, 0),
			Children = { new TextBlock { Text = label, FontSize = 13, Opacity = 0.75 }, input },
		};
	}
}
