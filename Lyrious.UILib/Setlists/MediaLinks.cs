using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Lyrious.UILib.Setlists
{
	/// <summary>Where a song's media link goes: a site, or a sound file kept on the device.</summary>
	public enum MediaSite { Other, Spotify, YouTube, Sound }

	/// <summary>
	/// A sound file on the device (see <see cref="ISoundStore"/>): its id there, its file name as it was
	/// added, and where in it the song starts and stops (seconds from its start; null for its start and
	/// its end), set in the waveform view (see <see cref="WaveformView"/>).
	/// </summary>
	public sealed record SoundFile(string Id, string Name, double? Start = null, double? Stop = null);

	/// <summary>
	/// A song's media link (a recording to listen to or watch): which site it's on, its icon, and
	/// searches on Spotify and YouTube for a song.
	/// </summary>
	public static partial class MediaLinks
	{
		// Line drawings in a 24 × 24 box, like the app's other icons.
		private const string NoteIcon = "M9,18 V5 L19,3 V16 M9,18 A3,3 0 1 1 3,18 A3,3 0 1 1 9,18 M19,16 A3,3 0 1 1 13,16 A3,3 0 1 1 19,16";
		private const string SpotifyIcon = "M12,2 A10,10 0 1 1 11.99,2 Z M6.8,9.4 Q12,7.8 17.4,10.4 M7.4,12.7 Q12,11.5 16.4,13.6 M8,15.9 Q11.6,15 15,16.6";
		private const string YouTubeIcon = "M5,5.5 H19 Q21.5,5.5 21.5,8 V16 Q21.5,18.5 19,18.5 H5 Q2.5,18.5 2.5,16 V8 Q2.5,5.5 5,5.5 Z M10,9 L15,12 L10,15 Z";

		// A sound file: the waveform of the Sound files page.
		private const string WaveformIcon = AppIcons.SoundFiles;
		private const string StopIcon = "M7,7 H17 V17 H7 Z";

		// "sound:<id>/<file name>": a sound file kept on the device, by its id there.
		private const string SoundPrefix = "sound:";

		/// <summary>The sound files that can be kept, by their extensions.</summary>
		public static readonly string[] SoundExtensions = [".mp3", ".m4a", ".aac", ".wav", ".ogg", ".oga", ".opus", ".flac", ".webm"];

		/// <summary>The largest sound file kept (100 MB).</summary>
		public const long MaxSoundSize = 100L * 1024 * 1024;

		public static bool IsSoundFile(string fileName) => Array.Exists(SoundExtensions, extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

		/// <summary>
		/// The media link of a sound file kept on the device, at most <paramref name="maxLength"/> long (the
		/// name shortened if need be): "sound:id/name", and where the song starts and stops in it, as a
		/// media fragment ("#t=12.5,245" in seconds; one left out is the sound's start or end).
		/// </summary>
		public static string SoundLink(SoundFile sound, int maxLength)
		{
			string Seconds(double? value) => value is { } seconds ? Math.Round(seconds, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
			var times = sound.Start is > 0 || sound.Stop != null ? $"#t={Seconds(sound.Start is > 0 ? sound.Start : null)},{Seconds(sound.Stop)}" : "";
			// One link per line (see Split): a line break in the name would split it.
			var start = $"{SoundPrefix}{sound.Id}/";
			var name = sound.Name.ReplaceLineEndings(" ");
			var room = Math.Max(0, maxLength - start.Length - times.Length);
			return start + (name.Length > room ? name[..room] : name) + times;
		}

		// A sound link's start and stop at its end: "#t=12.5,245", either part left out.
		[System.Text.RegularExpressions.GeneratedRegex(@"#t=(?<start>\d+(?:\.\d+)?)?,(?<stop>\d+(?:\.\d+)?)?$")]
		private static partial System.Text.RegularExpressions.Regex SoundTimes();

		/// <summary>
		/// A song's media links: its media link field holds one per line (a Spotify or YouTube link, a
		/// sound file on the device, another web address), in the order they were added.
		/// </summary>
		public static IReadOnlyList<string> Split(string? mediaLink)
			=> (mediaLink ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		/// <summary>The links as a song's media link field: one per line.</summary>
		public static string Join(IEnumerable<string> links) => string.Join('\n', links);

		/// <summary>
		/// The media link field with <paramref name="link"/> added at the end, if it isn't there yet;
		/// null with the reason when it can't be (the field can't be longer than <paramref name="maxLength"/>).
		/// </summary>
		public static string? With(string? mediaLink, string link, int maxLength, out string? problem)
		{
			problem = null;
			var links = Split(mediaLink).ToList();
			var added = link.Trim();
			if (links.Contains(added, StringComparer.Ordinal))
			{
				return Join(links);
			}

			links.Add(added);
			var joined = Join(links);
			if (joined.Length > maxLength)
			{
				problem = "There's no room for another media link on the song; remove one first.";
				return null;
			}

			return joined;
		}

		/// <summary>The media link field without <paramref name="link"/>.</summary>
		public static string Without(string? mediaLink, string link)
			=> Join(Split(mediaLink).Where(other => other != link.Trim()));

		/// <summary>The sound files the media link field names.</summary>
		public static IEnumerable<SoundFile> SoundsOf(string? mediaLink)
			=> Split(mediaLink).Select(SoundOf).OfType<SoundFile>();

		/// <summary>
		/// Whether pasted or dropped text is a media link: a sound file's link, a web address (with
		/// http:// or https://), a spotify: link, or a Spotify or YouTube address without its https://.
		/// </summary>
		public static bool IsMediaLink(string? text)
		{
			var trimmed = text?.Trim();
			if (string.IsNullOrEmpty(trimmed) || trimmed.Contains('\n') || trimmed.Contains(' '))
			{
				return SoundOf(trimmed) != null;
			}

			return SoundOf(trimmed) != null
			       || (Parse(trimmed) is { } uri
			           && (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			               || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			               || trimmed.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase)
			               || SiteOf(uri) is MediaSite.Spotify or MediaSite.YouTube));
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
			double? start = null, stop = null;
			if (SoundTimes().Match(rest) is { Success: true } times)
			{
				static double? Seconds(System.Text.RegularExpressions.Group group)
					=> group.Success ? double.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
				start = Seconds(times.Groups["start"]);
				stop = Seconds(times.Groups["stop"]);
				rest = rest[..times.Index];
			}

			var slash = rest.IndexOf('/');
			var id = slash < 0 ? rest : rest[..slash];
			return id.Length == 0 ? null : new SoundFile(id, slash < 0 ? id : rest[(slash + 1)..], start, stop);
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

		/// <summary>
		/// Where to play the link so that only that song plays: a YouTube video or Spotify track (or
		/// episode) in the site's embed player, which stops at the end instead of going on to another
		/// song (a YouTube start time is kept). Anything else (an album, a playlist, a short
		/// spotify.link, another site) is opened as it is.
		/// <para>
		/// The embed players have limits: a YouTube video whose owner doesn't allow embedding won't play
		/// there, and Spotify's plays only a 30-second preview unless the browser is logged in to Spotify.
		/// </para>
		/// </summary>
		public static Uri PlayerFor(Uri uri)
		{
			switch (SiteOf(uri))
			{
				case MediaSite.YouTube when YouTubeVideo(uri) is { } video:
					var start = YouTubeStart(uri) is { } seconds ? $"&start={seconds}" : "";
					return new Uri($"https://www.youtube.com/embed/{Uri.EscapeDataString(video)}?autoplay=1&rel=0{start}");
				case MediaSite.Spotify when SpotifyItem(uri) is { } item:
					return new Uri($"https://open.spotify.com/embed/{item.Kind}/{Uri.EscapeDataString(item.Id)}");
				default:
					return uri;
			}
		}

		/// <summary>The video's id in a YouTube link (watch?v=, youtu.be/, shorts/, live/, embed/); null if none.</summary>
		private static string? YouTubeVideo(Uri uri)
		{
			var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (HostIs(uri, "youtu.be"))
			{
				return segments.Length > 0 ? segments[0] : null;
			}

			if (segments is ["shorts" or "live" or "embed" or "v", var id, ..])
			{
				return id;
			}

			var v = QueryValue(uri, "v");
			return string.IsNullOrWhiteSpace(v) ? null : v;
		}

		/// <summary>A YouTube link's start time in seconds (t= or start=, e.g. "90", "90s" or "1m30s"); null if none.</summary>
		private static int? YouTubeStart(Uri uri)
		{
			var t = QueryValue(uri, "t") ?? QueryValue(uri, "start");
			if (string.IsNullOrWhiteSpace(t))
			{
				return null;
			}

			if (int.TryParse(t, out var plain))
			{
				return plain;
			}

			var match = System.Text.RegularExpressions.Regex.Match(t, @"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$");
			if (!match.Success || match.Length == 0)
			{
				return null;
			}

			int Part(int group) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value) : 0;
			return Part(1) * 3600 + Part(2) * 60 + Part(3);
		}

		/// <summary>
		/// The track or episode in a Spotify link (open.spotify.com/track/…, also with an
		/// "intl-xx" part, or spotify:track:…); null for anything else.
		/// </summary>
		private static (string Kind, string Id)? SpotifyItem(Uri uri)
		{
			var parts = uri.Scheme == "spotify"
				? uri.OriginalString.Split(':', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray()
				: uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
					.SkipWhile(part => part.StartsWith("intl-", StringComparison.OrdinalIgnoreCase)).ToArray();
			return parts is [var kind, var id, ..] && kind.ToLowerInvariant() is "track" or "episode" && id.Length > 0
				? (kind.ToLowerInvariant(), id)
				: null;
		}

		/// <summary>A query parameter's value in the link (decoded); null if it isn't there.</summary>
		private static string? QueryValue(Uri uri, string name)
		{
			foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
			{
				var equals = pair.IndexOf('=');
				var key = equals < 0 ? pair : pair[..equals];
				if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
				{
					return Uri.UnescapeDataString(equals < 0 ? "" : pair[(equals + 1)..]);
				}
			}

			return null;
		}

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
		/// <param name="artist">The song's artist; when empty, the lyrics' {artist: …} (or subtitle) is used.</param>
		public static string Query(string name, string lyrics, string? artist = null)
		{
			artist = string.IsNullOrWhiteSpace(artist) ? ChordPro.DirectiveValue(lyrics, "artist", "subtitle", "st") : artist.Trim();
			return string.IsNullOrWhiteSpace(artist) ? name.Trim() : $"{name.Trim()} {artist}";
		}

		/// <summary>
		/// The icon for a link on the site: Spotify's and YouTube's in their own colours, a waveform for
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
					MediaSite.Sound => playing ? StopIcon : WaveformIcon,
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
					// In the button's text colour, as the theme gives it (also when highlighted).
					icon.Bind(Shape.StrokeProperty, icon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
					break;
			}

			return icon;
		}
	}
}
