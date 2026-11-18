using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Lyrious.UILib.Setlists
{
	/// <summary>Where a song's media link goes: a site, or a sound file kept on the device.</summary>
	public enum MediaSite { Other, Spotify, YouTube, Sound }

	/// <summary>A sound file on the device (see <see cref="ISoundStore"/>): its id there, and its file name as it was added.</summary>
	public sealed record SoundFile(string Id, string Name);

	/// <summary>
	/// A song's media link (a recording to listen to or watch): which site it's on, its icon, and
	/// searches on Spotify and YouTube for a song.
	/// </summary>
	public static class MediaLinks
	{
		// Line drawings in a 24 × 24 box, like the app's other icons.
		private const string NoteIcon = "M9,18 V5 L19,3 V16 M9,18 A3,3 0 1 1 3,18 A3,3 0 1 1 9,18 M19,16 A3,3 0 1 1 13,16 A3,3 0 1 1 19,16";
		private const string SpotifyIcon = "M12,2 A10,10 0 1 1 11.99,2 Z M6.8,9.4 Q12,7.8 17.4,10.4 M7.4,12.7 Q12,11.5 16.4,13.6 M8,15.9 Q11.6,15 15,16.6";
		private const string YouTubeIcon = "M5,5.5 H19 Q21.5,5.5 21.5,8 V16 Q21.5,18.5 19,18.5 H5 Q2.5,18.5 2.5,16 V8 Q2.5,5.5 5,5.5 Z M10,9 L15,12 L10,15 Z";

		private const string SpeakerIcon = "M4,9 H8 L13,5 V19 L8,15 H4 Z M16.5,8.5 A5,5 0 0 1 16.5,15.5 M19,6 A8.5,8.5 0 0 1 19,18";
		private const string StopIcon = "M7,7 H17 V17 H7 Z";

		// "sound:<id>/<file name>": a sound file kept on the device, by its id there.
		private const string SoundPrefix = "sound:";

		/// <summary>The sound files that can be kept, by their extensions.</summary>
		public static readonly string[] SoundExtensions = [".mp3", ".m4a", ".aac", ".wav", ".ogg", ".oga", ".opus", ".flac", ".webm"];

		/// <summary>The largest sound file kept (100 MB).</summary>
		public const long MaxSoundSize = 100L * 1024 * 1024;

		public static bool IsSoundFile(string fileName) => Array.Exists(SoundExtensions, extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

		/// <summary>The media link of a sound file kept on the device, at most <paramref name="maxLength"/> long.</summary>
		public static string SoundLink(SoundFile sound, int maxLength)
		{
			var link = $"{SoundPrefix}{sound.Id}/{sound.Name}";
			return link.Length > maxLength ? link[..maxLength] : link;
		}

		/// <summary>The sound file the link names; null when it's not one.</summary>
		public static SoundFile? SoundOf(string? link)
		{
			var text = link?.Trim();
			if (text == null || !text.StartsWith(SoundPrefix, StringComparison.Ordinal))
			{
				return null;
			}

			var rest = text[SoundPrefix.Length..];
			var slash = rest.IndexOf('/');
			var id = slash < 0 ? rest : rest[..slash];
			return id.Length == 0 ? null : new SoundFile(id, slash < 0 ? id : rest[(slash + 1)..]);
		}

		/// <summary>A new id for a sound file: unique, with the file's extension (so a player knows what it is).</summary>
		public static string NewSoundId(string fileName)
			=> Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(fileName).ToLowerInvariant();

		/// <summary>The link as a web address it can open; null when it isn't one (or there's none).</summary>
		public static Uri? Parse(string? link)
		{
			var text = link?.Trim();
			if (string.IsNullOrEmpty(text) || SoundOf(text) != null)
			{
				return null;
			}

			// "open.spotify.com/track/…" without the scheme, as people sometimes copy it.
			if (!text.Contains("://", StringComparison.Ordinal) && !text.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
			{
				text = "https://" + text;
			}

			return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "spotify" ? uri : null;
		}

		public static MediaSite SiteOf(Uri? uri)
		{
			if (uri == null)
			{
				return MediaSite.Other;
			}

			if (uri.Scheme == "spotify" || HostIs(uri, "spotify.com") || HostIs(uri, "spotify.link"))
			{
				return MediaSite.Spotify;
			}

			return HostIs(uri, "youtube.com") || HostIs(uri, "youtu.be") ? MediaSite.YouTube : MediaSite.Other;
		}

		private static bool HostIs(Uri uri, string domain)
			=> uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

		/// <summary>A search on the site for the song (its name, and the artist when it's known).</summary>
		public static Uri Search(MediaSite site, string query) => site switch
		{
			MediaSite.Spotify => new Uri("https://open.spotify.com/search/" + Uri.EscapeDataString(query)),
			_ => new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query)),
		};

		/// <summary>
		/// What to search for: the song's name, and its artist if the lyrics name one (ChordPro's
		/// {artist: …}, or {subtitle: …} as it's often used for the artist).
		/// </summary>
		public static string Query(string name, string lyrics)
		{
			var artist = ChordPro.DirectiveValue(lyrics, "artist", "subtitle", "st");
			return string.IsNullOrWhiteSpace(artist) ? name.Trim() : $"{name.Trim()} {artist}";
		}

		/// <summary>
		/// The icon for a link on the site: Spotify's and YouTube's in their own colours, a speaker for
		/// a sound file (a stop sign while it plays), else a note in the theme's text colour.
		/// </summary>
		public static Control Icon(MediaSite site, bool playing = false)
		{
			var icon = new Path
			{
				Data = Geometry.Parse(site switch
				{
					MediaSite.Spotify => SpotifyIcon,
					MediaSite.YouTube => YouTubeIcon,
					MediaSite.Sound => playing ? StopIcon : SpeakerIcon,
					_ => NoteIcon,
				}),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 24,
				Height = 24,
				Stretch = Stretch.Uniform,
			};

			switch (site)
			{
				case MediaSite.Spotify:
					icon.Stroke = new SolidColorBrush(Color.Parse("#1DB954"));
					break;
				case MediaSite.YouTube:
					icon.Stroke = new SolidColorBrush(Color.Parse("#FF0033"));
					break;
				default:
					icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable(LyriousTheme.ForegroundBrush));
					break;
			}

			return icon;
		}
	}
}
