using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// A song's tempo, beat and key in a list row (the songbook, setlists, the right drawer), with the
	/// key moved by the member's own transpose and its value beside it ("A +2"). It follows when the
	/// member changes the transpose, here or on another device.
	/// </summary>
	public sealed class SongInfoLine : Decorator
	{
		private readonly SongDetails song;
		private readonly string? note;
		private readonly double fontSize;
		private readonly bool centered;

		/// <param name="note">Shown first, e.g. "in setlist" (see <see cref="PageLayout.SongInfoText"/>).</param>
		/// <param name="centered">Each part in the middle of the line (e.g. beside a small caption), rather than on one baseline.</param>
		public SongInfoLine(SongDetails song, string? note = null, double fontSize = 14, bool centered = false)
		{
			this.centered = centered;
			this.song = song;
			this.note = note;
			this.fontSize = fontSize;
			VerticalAlignment = VerticalAlignment.Center;
			// Without the transpose until it's read, so the row isn't empty meanwhile.
			Child = PageLayout.SongInfoText(song, note, fontSize, centered: centered);
		}

		/// <summary>The member's transpose of a song changed (here or on another device): its lines show it. Called on the UI thread.</summary>
		public static void TransposeChanged(Guid songId) => Changed?.Invoke(songId);

		private static event Action<Guid>? Changed;

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			Changed += OnChanged;
			_ = ShowTransposeAsync();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			Changed -= OnChanged;
		}

		private void OnChanged(Guid songId)
		{
			if (songId == song.Id)
			{
				_ = ShowTransposeAsync();
			}
		}

		/// <summary>Shows the key moved by the member's own transpose, with its value as large as the key.</summary>
		private async Task ShowTransposeAsync()
		{
			try
			{
				var transpose = (await App.SetlistService.GetMySongSettingAsync(song.Id)).ActiveTranspose;
				Child = PageLayout.SongInfoText(song, note, fontSize, transpose, transposeBesideKey: true, centered: centered);
			}
			catch (Exception ex)
			{
				// Shown without the transpose then; not worth bothering the member about.
				System.Diagnostics.Debug.WriteLine($"Couldn't read the member's own song settings: {ex.Message}");
			}
		}
	}
}
