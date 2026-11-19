using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>A song read from a file: its name and lyrics (chords included).</summary>
	public sealed record ImportedSong(string Name, string Lyrics);

	/// <summary>Imports a song from a text or ChordPro file the user picks.</summary>
	public static partial class SongFiles
	{
		/// <summary>The most the server stores for a song's lyrics.</summary>
		private const int MaxLyricsLength = 5000;

		/// <summary>
		/// Lets the user pick a file; its ChordPro title (or the file name) and text become the song.
		/// Returns null if no file was picked. Throws <see cref="InvalidOperationException"/> with a
		/// message for the user when files can't be opened here, the file can't be read, or it's too long.
		/// </summary>
		public static async Task<ImportedSong?> PickAsync(Control anchor)
		{
			if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { CanOpen: true } storage)
			{
				throw new InvalidOperationException(L.T("Opening files isn't possible here."));
			}

			var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = L.T("Import a song"),
				AllowMultiple = false,
				FileTypeFilter =
				[
					new FilePickerFileType(L.T("Lyrics and chords")) { Patterns = ["*.txt", "*.cho", "*.crd", "*.chopro", "*.pro"], MimeTypes = ["text/plain"] },
					FilePickerFileTypes.All,
				],
			});
			if (files.Count == 0)
			{
				return null;
			}

			string text;
			try
			{
				await using var stream = await files[0].OpenReadAsync();
				using var reader = new StreamReader(stream);
				text = (await reader.ReadToEndAsync()).Replace("\r\n", "\n").Trim();
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(L.T("Couldn't read the file. {0}", ex.Message), ex);
			}

			var (title, lyrics) = SplitTitle(text);
			if (lyrics.Length > MaxLyricsLength)
			{
				// Rather than cutting it off without saying so.
				throw new InvalidOperationException(L.T("The file is too long: {0} characters, and a song holds at most {1}.", lyrics.Length, MaxLyricsLength));
			}

			return new ImportedSong(title ?? Path.GetFileNameWithoutExtension(files[0].Name), lyrics);
		}

		/// <summary>Takes a ChordPro title directive (e.g. "{title: Blackbird}") out of the text, if there is one.</summary>
		private static (string? Title, string Lyrics) SplitTitle(string text)
		{
			var match = TitleDirective().Match(text);
			if (!match.Success)
			{
				return (null, text);
			}

			var lyrics = text.Remove(match.Index, match.Length).Trim('\n');
			return (match.Groups[1].Value.Trim(), lyrics);
		}

		[GeneratedRegex(@"^\{(?:title|t):\s*(.+?)\}[ \t]*\n?", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
		private static partial Regex TitleDirective();
	}
}
