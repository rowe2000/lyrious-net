using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The app's embedded media player (a Spotify or YouTube song in its embed player, see
	/// <see cref="MediaLinks.PlayerFor"/>): a layer over the whole app, so the song plays on whichever
	/// page is shown. It shows under the media button that started it; a click outside it (or the
	/// button again, or leaving that button's page) puts it out of sight, and the song plays on. It
	/// stops when another song is started (<see cref="StopUnlessSong"/>), another link is played, or
	/// in the player itself.
	/// <para>
	/// While out of sight the player stays in its place in the window, only moved far off, so its
	/// web view isn't torn down (moving a native web view to another place would stop it).
	/// </para>
	/// </summary>
	public sealed class MediaPlayerLayer : Canvas
	{
		/// <summary>The app's player layer (set by the signed-in app); null before that.</summary>
		public static MediaPlayerLayer? Current { get; set; }

		private NativeWebView? web;
		private Uri? source;
		private Control? anchor;
		private TopLevel? window;

		public MediaPlayerLayer()
		{
			// Presses go through the layer to the app, except on the player itself.
			ClipToBounds = false;
			watch.Tick += (_, _) => AskWhetherPlaying();
		}

		/// <summary>Whether the player shows (under its media button).</summary>
		public bool IsShown { get; private set; }

		/// <summary>The embed player's address playing now (shown or not); null when none plays.</summary>
		public Uri? Source => web != null ? source : null;

		/// <summary>The song whose link is playing; null when nothing plays.</summary>
		public Guid? SongId { get; private set; }

		/// <summary>Whether the player is playing now (started or paused in the player itself), as far as can be told.</summary>
		public bool IsPlaying { get; private set; }

		/// <summary>Raised when the player starts or stops playing (play or pause pressed in it, or the song ended).</summary>
		public event Action<bool>? PlayingChanged;

		// Asks the player's page now and then whether it plays (the page doesn't say so by itself).
		private readonly Avalonia.Threading.DispatcherTimer watch = new() { Interval = TimeSpan.FromMilliseconds(500) };
		private bool asking;

		/// <summary>
		/// Whether the player's page plays: a video or audio element that plays (YouTube's), else the
		/// play/pause button's label ("Pause" while it plays: Spotify's, whose audio isn't on the page).
		/// </summary>
		private const string PlayingScript =
			"(() => { const media = [...document.querySelectorAll('video,audio')];" +
			" if (media.some(m => !m.paused && !m.ended)) return 'playing';" +
			" const b = document.querySelector('[data-testid=\"play-pause-button\"]');" +
			" if (b) return (b.getAttribute('aria-label') || '').toLowerCase().startsWith('paus') ? 'playing' : 'paused';" +
			" return 'paused'; })()";

		private async void AskWhetherPlaying()
		{
			if (asking || web is not { } view)
			{
				return;
			}

			asking = true;
			try
			{
				var playing = (await view.InvokeScript(PlayingScript))?.Contains("playing", StringComparison.Ordinal) == true;
				if (web == view && playing != IsPlaying)
				{
					IsPlaying = playing;
					PlayingChanged?.Invoke(playing);
				}
			}
			catch (Exception ex)
			{
				// The page can't be asked (e.g. in a browser, where it's another site's): no telling.
				System.Diagnostics.Debug.WriteLine($"Couldn't ask the player whether it plays: {ex.Message}");
				watch.Stop();
			}
			finally
			{
				asking = false;
			}
		}

		/// <summary>
		/// Plays the embed player's address under <paramref name="button"/>; if it's already the one
		/// playing (for the same song), shows it there again, still playing.
		/// </summary>
		public void Show(Uri embed, Control button, Guid songId)
		{
			Anchor(button);
			if (web != null && source == embed && SongId == songId)
			{
				Place(shown: true);
				return;
			}

			Stop();
			var isSpotify = MediaLinks.SiteOf(embed) == MediaSite.Spotify;
			var view = new NativeWebView
			{
				Source = embed,
				Width = isSpotify ? 360 : 384,
				Height = isSpotify ? 152 : 216,
			};
			if (isSpotify)
			{
				// Spotify's player can be drawn shorter than the room it's given, leaving an empty
				// (black) strip below: once it's shown, the view takes the player's own height.
				view.NavigationCompleted += (_, e) =>
				{
					if (e.IsSuccess)
					{
						_ = FitToContentAsync(view);
					}
				};
			}

			web = view;
			source = embed;
			SongId = songId;
			Children.Add(view);
			Place(shown: true);
			watch.Start();
		}

		/// <summary>Puts the player out of sight; the song plays on.</summary>
		public void Hide() => Place(shown: false);

		/// <summary>Takes the player away, which stops the song.</summary>
		public void Stop()
		{
			watch.Stop();
			Children.Clear();
			web = null;
			source = null;
			SongId = null;
			IsShown = false;
			if (IsPlaying)
			{
				IsPlaying = false;
				PlayingChanged?.Invoke(false);
			}
		}

		/// <summary>Stops the player unless it plays <paramref name="songId"/>'s link (another song was started).</summary>
		public void StopUnlessSong(Guid songId)
		{
			if (SongId is { } playing && playing != songId)
			{
				Stop();
			}
		}

		/// <summary>The media button the player shows under; when it leaves the screen (another page), the player goes out of sight.</summary>
		private void Anchor(Control button)
		{
			if (anchor == button)
			{
				return;
			}

			if (anchor != null)
			{
				anchor.DetachedFromVisualTree -= OnAnchorGone;
			}

			anchor = button;
			button.DetachedFromVisualTree += OnAnchorGone;
		}

		private void OnAnchorGone(object? sender, VisualTreeAttachmentEventArgs e) => Hide();

		/// <summary>
		/// Puts the player under its media button, lined up with the button's right edge (shown), or
		/// far out of sight (not shown).
		/// </summary>
		private void Place(bool shown)
		{
			if (web is not { } view)
			{
				IsShown = false;
				return;
			}

			var corner = shown && anchor != null
				? anchor.TranslatePoint(new Point(anchor.Bounds.Width, anchor.Bounds.Height + 4), this)
				: null;
			IsShown = corner != null;
			if (corner is { } point)
			{
				SetLeft(view, Math.Max(0, point.X - view.Width));
				SetTop(view, Math.Max(0, point.Y));
				WatchPresses();
			}
			else
			{
				SetLeft(view, -20000);
				SetTop(view, -20000);
			}
		}

		/// <summary>Watches presses in the app's window, so one outside the player (and its button) puts it out of sight.</summary>
		private void WatchPresses()
		{
			// Presses inside the player go to its web page, not to the app, so they never get here.
			if (TopLevel.GetTopLevel(this) is { } top && window != top)
			{
				window?.RemoveHandler(PointerPressedEvent, OnPressed);
				window = top;
				top.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
			}
		}

		private void OnPressed(object? sender, PointerPressedEventArgs e)
		{
			if (!IsShown)
			{
				return;
			}

			bool In(Visual? area) => e.Source is Visual pressed && area != null && (pressed == area || area.IsVisualAncestorOf(pressed));

			// The media button hides the player itself (its click), so a press on it is left to that.
			if (!In(web) && !In(anchor))
			{
				Hide();
			}
		}

		/// <summary>
		/// Makes the web view as tall as what its page draws (the lowest edge of anything on it that's
		/// shorter than the view), if that's less. The page draws itself after it's loaded, so this
		/// looks a few times over the first seconds.
		/// </summary>
		private async Task FitToContentAsync(NativeWebView view)
		{
			const string MeasureScript =
				"(() => { let bottom = 0; for (const el of document.querySelectorAll('body *')) {" +
				" const r = el.getBoundingClientRect(); if (r.height > 0 && r.height < innerHeight - 1) bottom = Math.max(bottom, r.bottom); }" +
				" return Math.ceil(bottom); })()";

			foreach (var delay in new[] { 300, 1000, 2500 })
			{
				await Task.Delay(delay);
				if (web != view)
				{
					return; // Stopped (or another song) meanwhile.
				}

				try
				{
					var result = (await view.InvokeScript(MeasureScript))?.Trim('"', ' ');
					if (double.TryParse(result, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height)
					    && height >= 40 && height < view.Height)
					{
						view.Height = height;
					}
				}
				catch (Exception ex)
				{
					// The page couldn't be measured: it keeps its size.
					System.Diagnostics.Debug.WriteLine($"Couldn't measure the player: {ex.Message}");
					return;
				}
			}
		}
	}
}
