using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's lyrics in large type, scrollable, with its key, tempo and beat. The pencil button
	/// switches to editing the lyrics in place; Save stores them, Cancel drops the changes.
	/// </summary>
	public sealed class LyricScrollView : UserControl
	{
		// A pencil: the body, its tip, and the line where the tip meets the body.
		private const string EditIcon = "M4,20 L8,19 L19,8 L16,5 L5,16 Z M14,7 L17,10";

		/// <summary>The most the server stores for a song's lyrics.</summary>
		private const int MaxLyricsLength = 5000;

		private readonly ContentControl body = new();
		private readonly Button editButton;
		private readonly Func<string, Task<SongDetails>> saveLyrics;
		private SongDetails song;

		/// <summary>Raised when the user wants to go back (to the song's setlist, or the songbook).</summary>
		public event Action? BackRequested;

		/// <param name="backText">The back link's text, e.g. "‹ " and the setlist's name.</param>
		/// <param name="saveLyrics">Stores new lyrics for the song and returns the updated song; throws if that fails.</param>
		public LyricScrollView(SongDetails song, string backText, Func<string, Task<SongDetails>> saveLyrics)
		{
			this.song = song;
			this.saveLyrics = saveLyrics;

			editButton = HomeView.IconButton(EditIcon, "Edit lyrics");
			editButton.Click += (_, _) => ShowEditor();

			ShowLyrics();
			Content = PageLayout.Page(
				song.Name,
				body,
				backText: backText,
				back: () => BackRequested?.Invoke(),
				subtitle: PageLayout.SongInfo(song),
				titleAction: editButton);
		}

		private void ShowLyrics()
		{
			var hasLyrics = !string.IsNullOrWhiteSpace(song.Lyrics);
			body.Content = new ScrollViewer
			{
				Content = new TextBlock
				{
					Text = hasLyrics ? song.Lyrics : "There are no lyrics for this song yet. Add them with the pencil button.",
					FontSize = hasLyrics ? 24 : 16,
					LineHeight = hasLyrics ? 36 : double.NaN,
					Opacity = hasLyrics ? 1 : 0.7,
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(0, 0, 0, 48),
				},
			};
			editButton.IsVisible = true;
		}

		/// <summary>The lyrics in a text box, with Save and Cancel (and the error, if saving fails) below.</summary>
		private void ShowEditor()
		{
			editButton.IsVisible = false;

			var editor = new TextBox
			{
				Text = song.Lyrics,
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MaxLength = MaxLyricsLength,
				FontSize = 18,
				VerticalContentAlignment = VerticalAlignment.Top,
				PlaceholderText = "Type or paste the lyrics",
			};
			var save = new Button { Content = "Save" };
			save.Classes.Add("accent");
			var cancel = new Button { Content = "Cancel" };
			var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
			error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable(LyriousTheme.ErrorBrush));

			var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 16), Children = { save, cancel, error } };
			DockPanel.SetDock(buttons, Dock.Bottom);

			cancel.Click += (_, _) => ShowLyrics();
			save.Click += async (_, _) =>
			{
				save.IsEnabled = cancel.IsEnabled = editor.IsEnabled = false;
				error.IsVisible = false;
				try
				{
					song = await saveLyrics(editor.Text ?? string.Empty);
					ShowLyrics();
				}
				catch (Exception ex)
				{
					// The edits stay in the box, so nothing is lost; Save can be tried again.
					error.Text = $"Couldn't save. {ex.Message}";
					error.IsVisible = true;
					save.IsEnabled = cancel.IsEnabled = editor.IsEnabled = true;
				}
			};

			body.Content = new DockPanel { Children = { buttons, editor } };
			editor.Focus();
		}
	}
}
