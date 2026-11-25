using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrious.UILib.Localization;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// Plays a sound file from an item in a list (Sessions, Sound files) in the app: a play button that
	/// turns into a pause button while it plays (played again, it goes on from where it was paused), and
	/// a thin bar along the bottom of the item that shows how far it has played; dragging along the bar
	/// moves there. One sound plays at a time: starting another stops this one (forgetting where it was
	/// paused), and the bar shows at the other item instead. A file that isn't on this device yet (only
	/// on the server) shows a download button instead: pressed, it's downloaded and kept here, and then
	/// it's a play button.
	/// </summary>
	internal sealed class SoundItemPlayer
	{
		private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
		private const string PauseIcon = "M7,5 H10 V19 H7 Z M14,5 H17 V19 H14 Z";
		private const string DownloadIcon = "M10,3 H14 V11 H18 L12,17 L6,11 H10 Z M5,19 H19 V21 H5 Z";

		// The ids of the sound files on this device, listed once for all the items shown at a time (see NeedsDownloadAsync).
		private static (ISoundStore Sounds, DateTime At, Task<HashSet<string>> Ids)? here;

		// Whether it has to be downloaded before it plays (it isn't on this device).
		private bool needsDownload;

		/// <summary>Raised when it has been downloaded and kept on this device (see the download button).</summary>
		public event Action? Downloaded;

		// The sound paused, where, and how long it is: one at a time, as one plays at a time, and kept
		// while the list is made anew (e.g. when a file reaches the server).
		private static (string Id, TimeSpan At, TimeSpan? Duration)? paused;

		private readonly ISoundStore sounds;
		private readonly string id;
		private readonly Func<Task<bool>> play;
		private readonly string playTip;
		private readonly Path icon;
		// How far it has played, while it plays (it's played on the sound store's own thread).
		private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
		// The room for dragging along the bar: taller than the bar, so a finger finds it.
		private readonly Border dragArea;
		private bool dragging;

		/// <summary>The play button (a pause button while this sound plays).</summary>
		public Button Button { get; }

		/// <summary>The bar along the bottom of the item: how far the sound has played; shown while it plays or is paused.</summary>
		public ProgressBar Progress { get; } = new()
		{
			Minimum = 0,
			Maximum = 1,
			Height = 3,
			MinHeight = 3,
			CornerRadius = new CornerRadius(0),
			VerticalAlignment = VerticalAlignment.Bottom,
			IsHitTestVisible = false,
		};

		/// <param name="sounds">The device's sound files, which play them.</param>
		/// <param name="id">The sound file's id.</param>
		/// <param name="playTip">The play button's tooltip, e.g. "Play the band's recording of it".</param>
		/// <param name="play">
		/// Starts it (e.g. fetching it from the server first when it isn't on this device); false when it
		/// can't be had. Null: the sound store plays it straight away.
		/// </param>
		/// <param name="needsDownload">
		/// Whether it isn't on this device, so it's downloaded first (with the download button); null: looked up
		/// on this device when it's shown; false: it's here.
		/// </param>
		public SoundItemPlayer(ISoundStore sounds, string id, string playTip, Func<Task<bool>>? play = null, bool? needsDownload = false)
		{
			this.needsDownload = needsDownload == true;
			this.sounds = sounds;
			this.id = id;
			this.playTip = playTip;
			this.play = play ?? (() => sounds.PlayAsync(id));
			Button = HomeView.IconButton(PlayIcon, playTip);
			Button.VerticalAlignment = VerticalAlignment.Center;
			icon = (Path)Button.Content!;
			// Filled, as a player's play and pause buttons are.
			icon.Bind(Shape.FillProperty, icon.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
			icon.Width = icon.Height = 18;
			icon.Stretch = Stretch.Uniform;

			dragArea = new Border
			{
				Background = Brushes.Transparent,
				Height = 16,
				VerticalAlignment = VerticalAlignment.Bottom,
				Cursor = new Cursor(StandardCursorType.Hand),
				IsVisible = false,
				Child = Progress,
			};
			ToolTip.SetTip(dragArea, L.T("Drag to move in the sound"));
			dragArea.PointerPressed += (_, e) =>
			{
				dragging = true;
				e.Pointer.Capture(dragArea);
				e.Handled = true;
				DragTo(e);
			};
			dragArea.PointerMoved += (_, e) =>
			{
				if (dragging)
				{
					DragTo(e);
				}
			};
			dragArea.PointerReleased += (_, e) =>
			{
				if (!dragging)
				{
					return;
				}

				dragging = false;
				e.Pointer.Capture(null);
				e.Handled = true;
				MoveTo(DragTo(e));
			};
			dragArea.PointerCaptureLost += (_, _) => dragging = false;

			timer.Tick += (_, _) => ShowProgress();
			void OnPlayingChanged() => Dispatcher.UIThread.Post(ShowPlaying);
			Button.AttachedToVisualTree += (_, _) =>
			{
				sounds.PlayingChanged += OnPlayingChanged;
				ShowPlaying();
			};
			if (needsDownload == null)
			{
				_ = LookUpAsync();
			}
			Button.DetachedFromVisualTree += (_, _) =>
			{
				sounds.PlayingChanged -= OnPlayingChanged;
				timer.Stop();
			};
			Button.Click += async (_, _) => await ToggleAsync();
		}

		/// <summary>Whether this sound is the one playing in the app.</summary>
		private bool IsPlaying => sounds.Playing == id;

		/// <summary>Where this sound was paused (and how long it is); null when it isn't paused.</summary>
		private (TimeSpan At, TimeSpan? Duration)? Paused => paused is { } p && p.Id == id ? (p.At, p.Duration) : null;

		/// <summary>Whether it's on this device: the download button when it isn't.</summary>
		private async Task LookUpAsync()
		{
			try
			{
				needsDownload = !(await IdsHereAsync(sounds)).Contains(id);
			}
			catch (Exception ex)
			{
				// Then it's fetched when it's played, as before.
				System.Diagnostics.Debug.WriteLine($"Couldn't list the sound files: {ex.Message}");
			}

			ShowPlaying();
		}

		/// <summary>The ids of the sound files on this device: listed once for the items shown together (a second), not by each.</summary>
		private static Task<HashSet<string>> IdsHereAsync(ISoundStore sounds)
		{
			if (here is { } listed && listed.Sounds == sounds && DateTime.UtcNow - listed.At < TimeSpan.FromSeconds(1))
			{
				return listed.Ids;
			}

			var ids = ListIdsAsync(sounds);
			here = (sounds, DateTime.UtcNow, ids);
			return ids;
		}

		private static async Task<HashSet<string>> ListIdsAsync(ISoundStore sounds)
			=> (await sounds.ListAsync()).Select(sound => sound.Id).ToHashSet();

		/// <summary>Downloads it from the server and keeps it on this device; then it's a play button.</summary>
		private async Task DownloadAsync()
		{
			Button.IsEnabled = false;
			ToolTip.SetTip(Button, L.T("Downloading…"));
			dragArea.IsVisible = true;
			Progress.IsIndeterminate = true;
			try
			{
				if (await SoundSync.FetchAsync(id))
				{
					needsDownload = false;
					here = null; // Listed again next time, with this one.
					Downloaded?.Invoke();
				}
				else
				{
					Tell(L.T("The sound file isn't on the server (yet)."));
				}
			}
			catch (Exception ex)
			{
				Tell(L.T("Couldn't download it. {0}", ex.Message));
			}
			finally
			{
				Button.IsEnabled = true;
				Progress.IsIndeterminate = false;
				ShowPlaying();
			}
		}

		/// <summary>Pauses it while it plays; otherwise plays it (stopping any other), from where it was paused; downloads it first when it isn't here.</summary>
		private async Task ToggleAsync()
		{
			if (needsDownload && !IsPlaying)
			{
				await DownloadAsync();
				return;
			}

			if (IsPlaying)
			{
				// Kept before stopping: the stop shows the item as it is then.
				paused = (id, sounds.Position ?? TimeSpan.Zero, sounds.Duration);
				sounds.Stop();
				return;
			}

			var from = Paused?.At;
			Button.IsEnabled = false;
			try
			{
				if (!await play())
				{
					Tell(L.T("The sound file isn't on this device or the server (yet)."));
				}
				else
				{
					if (paused?.Id == id)
					{
						paused = null;
					}

					if (from is { } at && at > TimeSpan.Zero)
					{
						sounds.Seek(at);
					}
				}
			}
			catch (Exception ex)
			{
				Tell(L.T("Couldn't play it. {0}", ex.Message));
			}
			finally
			{
				Button.IsEnabled = true;
				ShowPlaying();
			}
		}

		/// <summary>The button and the bar as the sound is: pause and the bar while it plays, play with the bar while paused, play alone otherwise.</summary>
		private void ShowPlaying()
		{
			var playing = IsPlaying;
			// Another started: this one's pause is forgotten.
			if (sounds.Playing is { } other && other != id && paused?.Id == id)
			{
				paused = null;
			}

			var download = needsDownload && !playing;
			icon.Data = Geometry.Parse(playing ? PauseIcon : download ? DownloadIcon : PlayIcon);
			var tip = playing ? L.T("Pause") : download ? L.T("Download it to this device, to play") : Paused != null ? L.T("Play on from where it was paused") : playTip;
			ToolTip.SetTip(Button, tip);
			Avalonia.Automation.AutomationProperties.SetName(Button, tip);
			dragArea.IsVisible = playing || Paused != null;
			if (playing)
			{
				timer.Start();
			}
			else
			{
				timer.Stop();
			}

			ShowProgress();
		}

		/// <summary>How far it has played (or where it was paused); a moving bar while its length isn't known (yet).</summary>
		private void ShowProgress()
		{
			if (dragging)
			{
				return;
			}

			if (!IsPlaying && timer.IsEnabled)
			{
				ShowPlaying();
				return;
			}

			var (position, duration) = IsPlaying ? (sounds.Position, sounds.Duration) : Paused is { } p ? (p.At, p.Duration) : (null, null);
			if (position is { } at && duration is { TotalSeconds: > 0 } length)
			{
				Progress.IsIndeterminate = false;
				Progress.Value = Math.Clamp(at.TotalSeconds / length.TotalSeconds, 0, 1);
			}
			else
			{
				Progress.IsIndeterminate = IsPlaying;
				Progress.Value = 0;
			}
		}

		/// <summary>The bar at where the pointer is along it; that place, from 0 (the start) to 1 (the end).</summary>
		private double DragTo(PointerEventArgs e)
		{
			var width = dragArea.Bounds.Width;
			var fraction = width > 0 ? Math.Clamp(e.GetPosition(dragArea).X / width, 0, 1) : 0;
			Progress.IsIndeterminate = false;
			Progress.Value = fraction;
			return fraction;
		}

		/// <summary>Moves the sound there (0 the start, 1 the end): at once while it plays, else where it goes on from.</summary>
		private void MoveTo(double fraction)
		{
			if (IsPlaying)
			{
				if (sounds.Duration is { } duration)
				{
					sounds.Seek(duration * fraction);
				}
			}
			else if (Paused is { Duration: { } length })
			{
				paused = (id, length * fraction, length);
			}

			ShowProgress();
		}

		private void Tell(string message)
		{
			ToolTip.SetTip(Button, message);
			ToolTip.SetIsOpen(Button, true);
		}

		/// <summary>
		/// The item's card with the bar along its bottom edge, from side to side under its padding.
		/// </summary>
		public Border Card(Control content, Thickness padding)
		{
			// Over the content, out to the card's edges (the card's rounded corners clip it).
			dragArea.Margin = new Thickness(-padding.Left, 0, -padding.Right, -padding.Bottom);
			var panel = new Grid { Children = { content, dragArea } };
			return new Border { Padding = padding, Child = panel, ClipToBounds = true };
		}
	}
}
