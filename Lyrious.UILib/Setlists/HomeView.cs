using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrious.UILib.Login;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Lyrious.UILib.Setlists
{
	/// <summary>
	/// The signed-in app: the current page with two drawers, opened from buttons in the top corners.
	/// The left drawer holds the member, buttons showing the current band, setlist and song (each opens a
	/// list on the page to choose another) and account actions; the right drawer lists the
	/// current setlist's songs with key, tempo and beat, for jumping to another song. Each drawer can be
	/// pinned: it then docks beside the page and stays open, instead of sliding over it and closing
	/// after use. Opens on the page chosen by <see cref="StartPage.DetermineAsync"/> and saves the
	/// selected setlist and song to the group, so the next login resumes there. Tapping the lyrics
	/// turns focus mode on or off: drawers and their buttons are hidden, leaving just the song.
	/// The band shown is the member's joined band (<c>Member.JoinedGroup</c>): the one they follow
	/// live. Joining another band in one session switches all the member's sessions to it. When another member changes its current setlist or song, or starts or stops playing,
	/// the app follows.
	/// </summary>
	public sealed class HomeView : UserControl
	{
		private const string MenuIcon = "M4,7 H20 M4,12 H20 M4,17 H20";
		// A list icon: three lines with a dot in front of each.
		private const string ListIcon = "M9,7 H20 M9,12 H20 M9,17 H20 M4.5,7 H5 M4.5,12 H5 M4.5,17 H5";
		private const string NoSong = "Choose a song";
		private const string AddIcon = "M12,5 V19 M5,12 H19";
		private const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";
		// A pushpin: head, body widening to a base, and the needle.
		private const string PinIcon = "M9,3 H15 M10,3 V9 L7,13 H17 L14,9 V3 M12,13 V21";

		private readonly ISetlistService service;
		private readonly TextBlock groupName = new() { Text = "Choose a band", FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly TextBlock setlistTitle = new() { Text = "Setlist", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
		private readonly TextBlock setlistSubtitle = new() { Opacity = 0.7, IsVisible = false };
		private readonly ContentControl setlistDrawerBody = new();
		private readonly TextBlock currentSetlistName = new() { Text = "Choose a setlist", FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly TextBlock currentSongName = new() { Text = NoSong, FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly Button chooseSetlistButton;
		private readonly Button chooseSongButton;
		private readonly Button findSongButton;
		private readonly Button playlogButton;
		private readonly Button chooseGroupButton;
		private readonly TextBlock syncStatus = new() { FontSize = 13, Opacity = 0.7 };
		private readonly Button addSongsButton = IconButton(AddIcon, "Add a song to the setlist");
		private readonly ContentControl page = new();
		private readonly SplitView menuDrawer;
		private readonly SplitView setlistDrawer;
		private readonly Grid topRow;
		private readonly PlaybackBar playbackBar = new() { IsVisible = false };

		// A short message over the bottom of the page, e.g. why something can't be done right now.
		private readonly TextBlock noticeText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
		private readonly Border notice;
		private int noticeVersion;

		// The pages shown before the current one, for the ✕ on the middle pages (newest last).
		private readonly List<Control> history = [];
		private bool goingBack;
		private const int HistoryLimit = 20;

		// A band change of song that arrived while playing, to follow once playing stops.
		private GroupSummary? pendingFollow;

		// The waiting change is a catch-up (band sync turned on): the band's values win over this member's.
		private bool pendingCatchUp;

		// The play being timed (added to the playlog on pressing play): its length is stored when
		// that lyrics page stops scrolling.
		private (LyricScrollView View, Task<Guid?> Play, DateTime Started)? timedPlay;

		/// <summary>
		/// Band sync (the globe in the play toolbar): follow the joined band's current song, setlist and
		/// play/stop, and send this member's. Off, the member picks songs and plays on their own.
		/// </summary>
		private bool bandSync = PlaybackSettings.BandSync;

		// The current setlist's songs, for the next song in the playback toolbar.
		private IReadOnlyList<SetlistEntry> playbackEntries = [];

		// A group the member joined in another session, to switch to once they're done editing.
		private Guid? pendingJoinedGroup;

		// Focus mode: only the lyrics page, without drawers or their buttons. How each drawer was
		// hidden is remembered, to bring it back afterwards; pinned drawers stay pinned.
		private bool focusMode;
		private DrawerHiding menuHiding;
		private DrawerHiding setlistHiding;

		private enum DrawerHiding { None, Closed, Invisible }

		// What the app is showing, so the setlist drawer knows which setlist and song are current.
		private GroupSummary? currentGroup;
		private SetlistSummary? currentSetlist;
		private Guid? currentEntryId;
		private Guid? currentSongId;

		// What the right drawer is showing: the setlist, and its song rows by entry id (for moving the highlight).
		private Guid? drawerSetlistId;
		private readonly Dictionary<Guid, Button> drawerRows = new();
		private int drawerLoad;

		/// <summary>Raised when the user wants to change their password.</summary>
		public event Action? ChangePasswordRequested;

		/// <summary>Raised when the user wants to see or change the server (host) the app syncs with.</summary>
		public event Action? SyncRequested;

		/// <summary>Raised when the user wants to log out.</summary>
		public event Action? LogOutRequested;

		/// <summary>Raised when the user wants the password saved on this device removed.</summary>
		public event Action? ForgetSavedPasswordRequested;

		/// <param name="offerForgetSavedPassword">Shows "Forget saved password" in the menu (when a password is saved and the app can remove it).</param>
		public HomeView(AuthenticatedUser user, ISetlistService service, bool offerForgetSavedPassword = false)
		{
			this.service = service;
			chooseSetlistButton = DrawerPicker("Setlist", currentSetlistName, "Choose a setlist", () =>
			{
				if (currentGroup is { } group)
				{
					_ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
				}
			});
			chooseSetlistButton.IsEnabled = false;
			chooseSongButton = DrawerPicker("Song", currentSongName, "Choose a song", () =>
			{
				if (currentGroup is { } group)
				{
					_ = RunAsync(async () => ShowSongs(group, await service.GetSongbookSongsAsync(group.Id)));
				}
			});
			chooseSongButton.IsEnabled = false;
			findSongButton = DrawerItem("Find song online");
			findSongButton.IsEnabled = false;
			findSongButton.Click += (_, _) => OpenInternetSearch();
			playlogButton = DrawerItem("Playlog");
			playlogButton.IsEnabled = false;
			playlogButton.Click += (_, _) =>
			{
				if (currentGroup is { } group)
				{
					CloseUnlessPinned(menuDrawer);
					_ = RunAsync(async () => page.Content = new PlaylogView(group, await service.GetPlaylogsAsync(group.Id)));
				}
			};
			chooseGroupButton = DrawerPicker("Band", groupName, "Choose a band", () => _ = RunAsync(ShowGroupsAsync));
			chooseGroupButton.Margin = new Thickness(0, 12, 0, 0);

			var menuButton = IconButton(MenuIcon, "Open menu");
			menuButton.HorizontalAlignment = HorizontalAlignment.Left;

			var setlistButton = IconButton(ListIcon, "Open setlist");
			setlistButton.HorizontalAlignment = HorizontalAlignment.Right;

			// The drawer buttons float over the top corners instead of taking a row of their own, so
			// pages start at the same height with or without them (as in focus mode). The page keeps
			// clear of the corners at the sides, so narrow windows don't put text under the buttons.
			topRow = new Grid { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Top, Children = { menuButton, setlistButton } };
			page.Margin = new Thickness(48, 0);

			setlistDrawer = Drawer(SplitViewPanePlacement.Right, 320);
			// The playback toolbar sits under the lyrics page, also in focus mode.
			DockPanel.SetDock(playbackBar, Dock.Bottom);
			notice = new Border
			{
				Child = noticeText,
				Padding = new Thickness(16, 10),
				CornerRadius = new CornerRadius(8),
				Margin = new Thickness(16, 0, 16, 64), // Above the playback toolbar.
				MaxWidth = 480,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Bottom,
				IsVisible = false,
				IsHitTestVisible = false,
			};
			notice.Bind(Border.BackgroundProperty, notice.GetResourceObservable(LyriousTheme.CardBrush));
			setlistDrawer.Content = new Grid { Children = { new DockPanel { Children = { playbackBar, page } }, topRow, notice } };
			playbackBar.PlayPauseRequested += PlayPause;
			playbackBar.RestartRequested += () => (page.Content as LyricScrollView)?.ScrollToStart();
			playbackBar.NextRequested += NextSong;
			playbackBar.SetBandSync(bandSync);
			// Longclick on the globe: band sync on, with this member's setlist and song sent to the band.
			playbackBar.BandSyncSendRequested += () =>
			{
				bandSync = true;
				PlaybackSettings.BandSync = true;
				playbackBar.SetBandSync(true);
				pendingFollow = null; // This member's values win: nothing of the band's waits to apply.
				pendingCatchUp = false;
				_ = SendToBandAsync();
			};
			playbackBar.BandSyncToggled += on =>
			{
				bandSync = on;
				PlaybackSettings.BandSync = on;
				if (on)
				{
					_ = CatchUpWithBandAsync();
				}
				else
				{
					// A band change that waited for the stop no longer applies.
					pendingFollow = null;
					pendingCatchUp = false;
				}
			};
			setlistDrawer.Pane = SetlistDrawerContent(setlistButton);

			menuDrawer = Drawer(SplitViewPanePlacement.Left, 280);
			menuDrawer.Content = setlistDrawer;
			menuDrawer.Pane = MenuDrawerContent(user, menuButton, offerForgetSavedPassword);

			menuButton.Click += (_, _) => menuDrawer.IsPaneOpen = true;
			setlistButton.Click += (_, _) =>
			{
				setlistDrawer.IsPaneOpen = true;
				_ = LoadSetlistDrawerAsync(centerSong: true);
			};
			// Opened again some other way (e.g. after focus mode): the current song in the middle too.
			setlistDrawer.PaneOpened += (_, _) => CenterDrawerSong();

			Content = menuDrawer;

			// A middle page's ✕: back to the page before.
			page.AddHandler(PageLayout.CloseRequestedEvent, (_, e) =>
			{
				e.Handled = true;
				GoBack();
			});

			// Focus mode belongs to the lyrics page: leaving it (e.g. an error page) ends focus mode.
			// So do the playback toolbar and scrolling.
			page.PropertyChanged += (_, e) =>
			{
				if (e.Property != ContentControl.ContentProperty)
				{
					return;
				}

				if (e.OldValue is LyricScrollView old)
				{
					old.SetAutoScroll(false);
				}

				// Remember the page left, for ✕ (pages only: not loading indicators or messages).
				if (!goingBack && e.OldValue is UserControl left && !ReferenceEquals(left, e.NewValue))
				{
					history.Remove(left);
					history.Add(left);
					if (history.Count > HistoryLimit)
					{
						history.RemoveAt(0);
					}
				}

				// Left the lyrics while playing: a band change of song that waited for the stop applies now.
				if (pendingFollow is { } follow && e.NewValue is not LyricScrollView { IsAutoScrolling: true } && e.NewValue is not ProgressBar)
				{
					pendingFollow = null;
					var catchUp = pendingCatchUp;
					pendingCatchUp = false;
					Dispatcher.UIThread.Post(() => _ = FollowGroupAsync(follow, catchUp));
				}

				var isLyrics = e.NewValue is LyricScrollView;
				playbackBar.IsVisible = isLyrics;

				// The member joined another group in another session while busy here: switch now.
				if (pendingJoinedGroup != null && !IsBusy(e.NewValue) && e.NewValue is not ProgressBar)
				{
					Dispatcher.UIThread.Post(SwitchToJoinedGroup);
				}
				if (focusMode && !isLyrics)
				{
					SetFocusMode(false);
				}
			};
			// Escape also ends focus mode (the view takes keyboard focus when focus mode starts).
			Focusable = true;
			KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Escape && focusMode)
				{
					SetFocusMode(false);
					e.Handled = true;
				}
			};

			// Follow the joined band's current setlist and song, and its play/stop, when another member changes them.
			service.GroupChanged += OnGroupChanged;
			service.PlaybackChanged += OnPlaybackChanged;
			service.StatusChanged += OnStatusChanged;
			service.JoinedGroupChanged += OnJoinedGroupChanged;
			OnStatusChanged();
			_ = StartLiveUpdatesAsync();

			_ = StartAsync();
		}

		/// <summary>Starts receiving other members' changes; the app works without them if the hub can't be reached.</summary>
		private async Task StartLiveUpdatesAsync()
		{
			try
			{
				await service.StartLiveUpdatesAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Live updates aren't available: {ex.Message}");
			}
		}

		/// <summary>Stops following other members' changes, e.g. before switching to another server.</summary>
		public void Close() => StopLiveUpdates();

		/// <summary>Stops following other members' changes (on logout); the service outlives this view.</summary>
		private void StopLiveUpdates()
		{
			service.GroupChanged -= OnGroupChanged;
			service.PlaybackChanged -= OnPlaybackChanged;
			service.StatusChanged -= OnStatusChanged;
			service.JoinedGroupChanged -= OnJoinedGroupChanged;
			_ = service.StopLiveUpdatesAsync();
		}

		private void OnJoinedGroupChanged(Guid groupId) => Dispatcher.UIThread.Post(() =>
		{
			if (currentGroup?.Id == groupId)
			{
				return;
			}

			// Don't pull the page away while the member is busy; switch when they move on.
			if (IsBusy(page.Content))
			{
				pendingJoinedGroup = groupId;
				return;
			}

			SwitchToJoinedGroup();
		});

		/// <summary>Opens the start page of the member's joined group (joined in another session).</summary>
		private void SwitchToJoinedGroup()
		{
			pendingJoinedGroup = null;
			_ = RunAsync(() => OpenStartPageAsync());
		}

		/// <summary>Pages where the member is in the middle of something that a switch would throw away.</summary>
		private static bool IsBusy(object? content)
			=> content is SongEditView or AddSongsView or InternetSongView or LyricScrollView { IsEditing: true };

		private void OnStatusChanged() => Dispatcher.UIThread.Post(() => syncStatus.Text = SyncStatusText.Short(service.Status));

		/// <summary>
		/// Band sync turned on: catch up with the band's current setlist and song, which win over the
		/// ones picked here while it was off. First what this device knows (at once), then again after
		/// fetching the server's latest, in case the band moved on meanwhile.
		/// </summary>
		private async Task CatchUpWithBandAsync()
		{
			try
			{
				if (await service.GetCurrentGroupAsync() is { } known)
				{
					await FollowGroupAsync(known, catchUp: true);
				}

				await service.SyncNowAsync();
				if (bandSync && await service.GetCurrentGroupAsync() is { } latest)
				{
					await FollowGroupAsync(latest, catchUp: true);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't catch up with the band: {ex.Message}");
			}
		}

		/// <summary>
		/// Band sync turned on with a longclick: this member's current setlist and song become the
		/// band's (the other members that follow switch to them), and so does play/stop on the lyrics page.
		/// </summary>
		private async Task SendToBandAsync()
		{
			if (currentGroup is not { } group || currentSetlist is not { } setlist)
			{
				ShowNotice("Band sync on; no setlist picked here to send");
				return;
			}

			try
			{
				if (currentEntryId is { } entryId && await service.GetEntryAsync(entryId) is { } entry)
				{
					await service.SelectSongAsync(group.Id, entry);
					ShowNotice($"Sent to the band: {entry.Song.Name}");
				}
				else
				{
					await service.SelectSetlistAsync(group.Id, setlist.Id);
					ShowNotice($"Sent to the band: {setlist.Name}");
				}

				if (page.Content is LyricScrollView lyrics)
				{
					await SendPlaybackAsync(lyrics.IsAutoScrolling);
				}
			}
			catch (Exception ex)
			{
				ShowNotice($"Couldn't send to the band: {ex.Message}");
			}
		}

		private async Task SendPlaybackAsync(bool playing)
		{
			if (!bandSync)
			{
				return;
			}

			try
			{
				await service.SendPlaybackAsync(playing);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't send play/stop to the band: {ex.Message}");
			}
		}

		// The window this view is in, for its keys (see OnLyricsKey).
		private TopLevel? keyWindow;

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			// Listened to on the whole window, so the keys also work when nothing has keyboard focus
			// (then they don't pass through this view). Tunnelling: seen before the focused control.
			keyWindow = TopLevel.GetTopLevel(this);
			keyWindow?.AddHandler(KeyDownEvent, OnLyricsKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			keyWindow?.RemoveHandler(KeyDownEvent, OnLyricsKey);
			keyWindow = null;
		}

		/// <summary>
		/// Keys on the lyrics page (not while editing or typing in a box): the space bar plays and stops,
		/// as the play/stop button does; up and down nudge the lyrics (4 bars for timed songs, else a
		/// line); and, when not playing, left and right go to the previous and next song in the setlist.
		/// Handled before the focused control sees them, so a focused button isn't also pressed by the
		/// space bar (the play button would undo it).
		/// </summary>
		private void OnLyricsKey(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			if (e.KeyModifiers != Avalonia.Input.KeyModifiers.None || page.Content is not LyricScrollView { IsEditing: false } lyrics
			    || e.Source is TextBox)
			{
				return;
			}

			switch (e.Key)
			{
				case Avalonia.Input.Key.Space:
					e.Handled = true;
					PlayPause();
					break;
				case Avalonia.Input.Key.Up:
					e.Handled = true;
					lyrics.Nudge(-1);
					break;
				case Avalonia.Input.Key.Down:
					e.Handled = true;
					lyrics.Nudge(1);
					break;
				case Avalonia.Input.Key.Left when !lyrics.IsAutoScrolling:
					e.Handled = true;
					StepSong(-1);
					break;
				case Avalonia.Input.Key.Right when !lyrics.IsAutoScrolling:
					e.Handled = true;
					StepSong(1);
					break;
			}
		}

		/// <summary>Play/pause on the lyrics page (the toolbar button, or the space bar).</summary>
		private void PlayPause()
		{
			if (page.Content is LyricScrollView lyrics)
			{
				var stopping = lyrics.IsAutoScrolling;
				var atEnd = lyrics.IsAtEnd;
				lyrics.SetAutoScroll(!stopping);
				if (lyrics.IsAutoScrolling)
				{
					StartPlay(lyrics);
				}

				// The other members that have joined the band start or stop too.
				_ = SendPlaybackAsync(lyrics.IsAutoScrolling);

				// Stopping at the very end of the lyrics goes on to the next song (a setting, on by default).
				if (stopping && atEnd && PlaybackSettings.NextSongOnStopAtEnd)
				{
					NextSong();
				}
			}
		}

		/// <summary>
		/// Pressing play: the song goes into the band's playlog (not when following another member's
		/// play, so it's logged once), and is timed until scrolling stops.
		/// </summary>
		private void StartPlay(LyricScrollView lyrics)
		{
			if (currentGroup is not { } group || currentSongId is not { } songId)
			{
				return;
			}

			timedPlay = (lyrics, AddPlayAsync(group.Id, songId), DateTime.Now);
		}

		private async Task<Guid?> AddPlayAsync(Guid groupId, Guid songId)
		{
			try
			{
				return await service.AddPlayAsync(groupId, songId);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't add the song to the playlog: {ex.Message}");
				return null;
			}
		}

		private async Task AddPlayTimeAsync(Task<Guid?> play, TimeSpan time)
		{
			try
			{
				if (await play is { } playId)
				{
					await service.AddPlayTimeAsync(playId, time);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't store how long the song was played: {ex.Message}");
			}
		}

		private void OnPlaybackChanged(Guid groupId, bool playing) => Dispatcher.UIThread.Post(() =>
		{
			// Only with band sync on, for the joined band, on its lyrics page (not while editing). Focus
			// mode follows, as when pressing play/pause here. Not sent on, so it doesn't bounce back and forth.
			if (bandSync && currentGroup?.Id == groupId && page.Content is LyricScrollView { IsEditing: false } lyrics)
			{
				lyrics.SetAutoScroll(playing);
			}
		});

		private void OnGroupChanged(GroupSummary group) => Dispatcher.UIThread.Post(() =>
		{
			if (bandSync)
			{
				_ = FollowGroupAsync(group);
			}
		});

		/// <summary>
		/// When another member changes the current setlist or song of the band this member has
		/// selected, follow: open the new current song, or the new setlist when no song in it is
		/// current. While playing, the new song plays on. Pages where the member is busy (editing,
		/// adding songs, searching, other lists) aren't replaced: the drawers and buttons switch, and
		/// the page stays until they move on.
		/// </summary>
		/// <param name="catchUp">
		/// Band sync was just turned on: the band's values replace this member's wherever they differ,
		/// also when the band has no current song (the setlist is shown) or no current setlist (the
		/// band's start page).
		/// </param>
		private async Task FollowGroupAsync(GroupSummary group, bool catchUp = false)
		{
			if (currentGroup?.Id != group.Id)
			{
				return;
			}

			var setlistChanged = catchUp
				? group.CurrentSetlistId != currentSetlist?.Id
				: group.CurrentSetlistId is { } newSetlistId && newSetlistId != currentSetlist?.Id;
			var songChanged = catchUp
				? group.CurrentSetlistItemId != currentEntryId
				: group.CurrentSetlistItemId is { } newItemId && newItemId != currentEntryId;
			if (!setlistChanged && !songChanged)
			{
				return;
			}

			// The song can't change while playing: follow once playing stops (the latest change wins;
			// a catch-up still waiting stays one).
			if (IsPlaying)
			{
				pendingFollow = group;
				pendingCatchUp |= catchUp;
				return;
			}

			if (catchUp && group.CurrentSetlistId == null)
			{
				// The band has no current setlist: its start page (or, while busy here, just the drawers).
				var replace = page.Content is SetlistView or SetlistsView or LyricScrollView { IsEditing: false };
				await RunAsync(() => OpenStartPageAsync(showPage: replace));
				return;
			}

			try
			{
				var setlist = setlistChanged ? await service.GetSetlistAsync(group.CurrentSetlistId!.Value) : currentSetlist;
				if (setlist == null || currentGroup?.Id != group.Id)
				{
					return;
				}

				var entry = group.CurrentSetlistItemId is { } itemId ? await service.GetEntryAsync(itemId) : null;
				if (entry?.SetlistId != setlist.Id)
				{
					entry = null;
				}

				if (!setlistChanged && entry == null && !catchUp)
				{
					return; // The new current song is gone already, or not in this setlist.
				}

				var canReplacePage = page.Content is SetlistView or SetlistsView or LyricScrollView { IsEditing: false };
				if (!canReplacePage)
				{
					SetCurrentSetlist(setlist);
					SetCurrentSong(entry?.Song, entry?.Id);
					if (setlistChanged)
					{
						playbackEntries = [];
					}

					RefreshSetlistDrawer(reload: setlistChanged);
					return;
				}

				if (entry != null)
				{
					ShowLyrics(group, setlist, entry);
				}
				else
				{
					SetCurrentSong(null, null);
					await RunAsync(() => ShowSetlistAsync(group, setlist));
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't follow the band's setlist or song: {ex.Message}");
			}
		}

		/// <summary>A drawer, unpinned: it slides over the page and closes when tapping outside it.</summary>
		private static SplitView Drawer(SplitViewPanePlacement placement, double width)
		{
			var drawer = new SplitView
			{
				DisplayMode = SplitViewDisplayMode.Overlay,
				PanePlacement = placement,
				OpenPaneLength = width,
				IsPaneOpen = false,
			};
			drawer.Bind(SplitView.PaneBackgroundProperty, drawer.GetResourceObservable(LyriousTheme.CardBrush));
			return drawer;
		}

		private static bool IsPinned(SplitView drawer) => drawer.DisplayMode == SplitViewDisplayMode.Inline;

		/// <summary>
		/// The ✕ and pin buttons for a drawer's header. Pinning docks the drawer beside the page
		/// (it stays open and the page makes room); unpinning makes it slide over the page again.
		/// ✕ hides the drawer but keeps it pinned, so it docks again when reopened. While a pinned
		/// drawer is showing, its open button in the top corner is hidden.
		/// </summary>
		private static StackPanel DrawerButtons(SplitView drawer, Button openButton, string name)
		{
			var pinButton = IconButton(PinIcon, $"Pin {name}");
			var closeButton = EscapeKey.MarkAsClose(IconButton(CloseIcon, $"Close {name}"));
			closeButton.Click += (_, _) => drawer.IsPaneOpen = false;

			void Update()
			{
				var pinned = IsPinned(drawer);
				// Upright and highlighted when pinned, tilted when not.
				((Path)pinButton.Content!).RenderTransform = pinned ? null : new RotateTransform(45);
				if (pinned)
				{
					pinButton.Classes.Add("accent");
				}
				else
				{
					pinButton.Classes.Remove("accent");
				}

				var description = pinned ? $"Unpin {name}" : $"Pin {name}";
				ToolTip.SetTip(pinButton, description);
				Avalonia.Automation.AutomationProperties.SetName(pinButton, description);
				openButton.IsVisible = !(pinned && drawer.IsPaneOpen);
			}

			pinButton.Click += (_, _) =>
			{
				drawer.DisplayMode = IsPinned(drawer) ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
				Update();
			};
			drawer.PaneOpened += (_, _) => Update();
			drawer.PaneClosed += (_, _) => Update();
			Update();

			return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { pinButton, closeButton } };
		}

		/// <summary>Closes the drawer after an action, unless it's pinned.</summary>
		private static void CloseUnlessPinned(SplitView drawer)
		{
			if (!IsPinned(drawer))
			{
				drawer.IsPaneOpen = false;
			}
		}

		/// <summary>
		/// A button at the top of the left drawer that shows what is current (the band, the setlist)
		/// under a small caption, and opens a list on the page to choose another.
		/// </summary>
		private Button DrawerPicker(string caption, TextBlock current, string description, Action choose)
		{
			var button = new Button
			{
				Content = new StackPanel
				{
					Spacing = 2,
					Children = { new TextBlock { Text = caption, FontSize = 13, Opacity = 0.75 }, current },
				},
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(12, 8),
			};
			ToolTip.SetTip(button, description);
			button.Click += (_, _) =>
			{
				CloseUnlessPinned(menuDrawer);
				choose();
			};
			return button;
		}

		/// <summary>Records the setlist the app is showing, and shows its name on the left drawer's button.</summary>
		private void SetCurrentSetlist(SetlistSummary setlist)
		{
			currentSetlist = setlist;
			currentSetlistName.Text = setlist.Name;
			addSongsButton.IsEnabled = true;
		}

		/// <summary>Records the song the app is showing (and its setlist entry, if it's shown from a setlist), for the Song button and the highlights.</summary>
		private void SetCurrentSong(SongDetails? song, Guid? entryId)
		{
			currentSongId = song?.Id;
			currentEntryId = entryId;
			currentSongName.Text = song?.Name ?? NoSong;
		}

		/// <summary>The left drawer: member, band, setlist and song at the top, account actions at the bottom.</summary>
		private Control MenuDrawerContent(AuthenticatedUser user, Button openButton, bool offerForgetSavedPassword)
		{
			var buttons = DrawerButtons(menuDrawer, openButton, "menu");
			DockPanel.SetDock(buttons, Dock.Right);

			var settings = DrawerItem("Settings");
			settings.Click += (_, _) =>
			{
				CloseUnlessPinned(menuDrawer);
				ShowSettings();
			};

			var sync = DrawerItem("Sync");
			// Under "Sync": whether the server can be reached and how many changes are waiting.
			sync.Content = new StackPanel
			{
				Spacing = 2,
				Children = { new TextBlock { Text = "Sync" }, syncStatus },
			};
			sync.Click += (_, _) =>
			{
				CloseUnlessPinned(menuDrawer);
				SyncRequested?.Invoke();
			};

			var changePassword = DrawerItem("Change password");
			changePassword.Click += (_, _) =>
			{
				CloseUnlessPinned(menuDrawer);
				ChangePasswordRequested?.Invoke();
			};

			var logOut = DrawerItem("Log out");
			logOut.Click += (_, _) =>
			{
				menuDrawer.IsPaneOpen = false;
				StopLiveUpdates();
				LogOutRequested?.Invoke();
			};

			var header = new StackPanel
			{
				Spacing = 4,
				Children =
				{
					new DockPanel { Children = { buttons, new TextBlock { Text = user.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } } },
					chooseGroupButton,
					chooseSetlistButton,
					chooseSongButton,
					findSongButton,
					playlogButton,
				},
			};
			DockPanel.SetDock(header, Dock.Top);

			// The account actions sit at the bottom of the drawer.
			var account = new StackPanel { Spacing = 4, Children = { settings, sync, changePassword } };
			DockPanel.SetDock(account, Dock.Bottom);

			if (offerForgetSavedPassword)
			{
				var forget = DrawerItem("Forget saved password");
				forget.Click += (_, _) =>
				{
					// Gone once used: there's nothing saved any more.
					account.Children.Remove(forget);
					ForgetSavedPasswordRequested?.Invoke();
				};
				account.Children.Add(forget);
			}

			account.Children.Add(logOut);

			return new DockPanel
			{
				Margin = new Thickness(16, 8, 8, 16),
				LastChildFill = false,
				Children = { header, account },
			};
		}

		/// <summary>An item of the right drawer's "+" menu, run for the current band and setlist.</summary>
		private MenuItem DrawerAddItem(string header, Func<GroupSummary, SetlistSummary, Task> add)
		{
			var item = new MenuItem { Header = header };
			item.Click += (_, _) =>
			{
				if (currentGroup is { } group && currentSetlist is { } setlist)
				{
					CloseUnlessPinned(setlistDrawer);
					_ = add(group, setlist);
				}
			};
			return item;
		}

		/// <summary>Fills in a new song (name, key, tempo, beat, lyrics); saving adds it to the songbook and the setlist.</summary>
		private void ShowNewSongEditor(GroupSummary group, SetlistSummary setlist)
		{
			var blank = new SongDetails(Guid.Empty, "", "", 120, "4/4", "");
			var view = new SongEditView(blank, async filled =>
			{
				var created = await service.CreateSongAsync(group.Id, filled.Name, filled.Lyrics, TimeSpan.Zero);
				return await service.UpdateSongAsync(filled with { Id = created.Id });
			}, title: "New song");
			view.Cancelled += () => _ = RunAsync(() => ShowSetlistAsync(group, setlist));
			view.Saved += song => _ = RunAsync(() => AddToSetlistAndShowAsync(group, setlist, song));
			page.Content = view;
		}

		/// <summary>
		/// Imports a song from a file into the songbook and the setlist. The file is picked before
		/// the loading indicator can show, so cancelling the picker leaves the page as it was.
		/// </summary>
		private async Task ImportSongToSetlistAsync(GroupSummary group, SetlistSummary setlist)
		{
			ImportedSong? imported;
			try
			{
				imported = await SongFiles.PickAsync(this);
			}
			catch (InvalidOperationException ex)
			{
				ShowMessage(ex.Message);
				return;
			}

			if (imported != null)
			{
				await RunAsync(async () =>
				{
					var song = await service.CreateSongAsync(group.Id, imported.Name, imported.Lyrics, TimeSpan.Zero);
					await AddToSetlistAndShowAsync(group, setlist, song);
				});
			}
		}

		private async Task AddToSetlistAndShowAsync(GroupSummary group, SetlistSummary setlist, SongDetails song)
		{
			await service.AddSongsAsync(setlist.Id, [song.Id]);
			await ShowSetlistAsync(group, setlist);
		}

		/// <summary>The right drawer: a fixed header (setlist name, song count, "+", pin and ✕) and the song list.</summary>
		private Control SetlistDrawerContent(Button openButton)
		{
			var buttons = DrawerButtons(setlistDrawer, openButton, "setlist");
			DockPanel.SetDock(buttons, Dock.Right);

			// "+" in front of pin and ✕: add songs from the songbook, as on the setlist page.
			addSongsButton.IsEnabled = false;
			// "+" opens a menu of ways to add a song; each puts the song at the end of the setlist.
			addSongsButton.Flyout = new MenuFlyout
			{
				Items =
				{
					DrawerAddItem("From the songbook…", (group, setlist) => RunAsync(async () =>
					{
						var entries = await service.GetEntriesAsync(setlist.Id);
						ShowAddSongs(group, setlist, await service.GetSongbookSongsAsync(group.Id), entries.Select(entry => entry.Song.Id).ToHashSet());
					})),
					DrawerAddItem("New empty song…", (group, setlist) =>
					{
						ShowNewSongEditor(group, setlist);
						return Task.CompletedTask;
					}),
					DrawerAddItem("Import from file…", ImportSongToSetlistAsync),
					DrawerAddItem("Find on the internet…", (group, setlist) => RunAsync(async () =>
						ShowInternetSearch(group, await service.GetSongbookSongsAsync(group.Id), setlist))),
				},
			};
			buttons.Children.Insert(0, addSongsButton);

			var header = new StackPanel
			{
				Spacing = 2,
				Margin = new Thickness(0, 0, 0, 12),
				Children = { new DockPanel { Children = { buttons, setlistTitle } }, setlistSubtitle },
			};
			DockPanel.SetDock(header, Dock.Top);

			return new DockPanel { Margin = new Thickness(16, 8, 8, 16), Children = { header, setlistDrawerBody } };
		}

		/// <summary>
		/// Fills the right drawer with the current setlist's songs, loaded fresh. While reloading the
		/// setlist it already shows, the old list stays up (no progress bar), so it doesn't flicker;
		/// the scroll position is kept too. A load that a newer one has overtaken is dropped.
		/// </summary>
		/// <param name="centerSong">Scrolls the list so the current song is in the middle once it's loaded (when the drawer opens).</param>
		private async Task LoadSetlistDrawerAsync(bool centerSong = false)
		{
			var load = ++drawerLoad;
			if (currentGroup is not { } group || currentSetlist is not { } setlist)
			{
				drawerSetlistId = null;
				drawerRows.Clear();
				SetSetlistHeader("Setlist", null);
				setlistDrawerBody.Content = DrawerMessage("No setlist is selected yet. Choose one from the list of setlists.");
				return;
			}

			var sameSetlist = drawerSetlistId == setlist.Id;
			if (!sameSetlist)
			{
				SetSetlistHeader(setlist.Name, null);
				setlistDrawerBody.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			}

			try
			{
				var entries = await service.GetEntriesAsync(setlist.Id);
				if (load != drawerLoad)
				{
					return;
				}

				SetSetlistHeader(setlist.Name, entries.Count == 1 ? "1 song" : $"{entries.Count} songs");
				drawerSetlistId = setlist.Id;
				drawerRows.Clear();
				if (entries.Count == 0)
				{
					setlistDrawerBody.Content = DrawerMessage("There are no songs in this setlist yet.");
					return;
				}

				var list = new StackPanel { Spacing = 6 };
				foreach (var entry in entries)
				{
					var row = SongRow(group, setlist, entry);
					drawerRows[entry.Id] = row;
					list.Children.Add(row);
				}

				if (sameSetlist && setlistDrawerBody.Content is ScrollViewer scroller)
				{
					scroller.Content = list;
				}
				else
				{
					setlistDrawerBody.Content = new ScrollViewer { Content = list };
				}

				if (centerSong)
				{
					CenterDrawerSong();
				}
			}
			catch (Exception ex)
			{
				if (load == drawerLoad)
				{
					drawerSetlistId = null;
					drawerRows.Clear();
					setlistDrawerBody.Content = DrawerMessage($"Couldn't load the songs. {ex.Message}");
				}
			}
		}

		/// <summary>Highlights the current song's row (and only it) without rebuilding the list.</summary>
		/// <summary>
		/// Scrolls the setlist drawer's list so the current song's row is in the middle (as near as the
		/// list's ends allow). Done after layout, when the rows have their places. Nothing if no song
		/// in the drawer's setlist is current.
		/// </summary>
		private void CenterDrawerSong() => Dispatcher.UIThread.Post(() =>
		{
			if (currentEntryId is not { } entryId
			    || !drawerRows.TryGetValue(entryId, out var row)
			    || setlistDrawerBody.Content is not ScrollViewer { Content: Control list } scroller
			    || row.TranslatePoint(new Point(0, row.Bounds.Height / 2), list) is not { } middle)
			{
				return;
			}

			var max = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
			scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(middle.Y - scroller.Viewport.Height / 2, 0, max));
		}, DispatcherPriority.Background);

		private void HighlightDrawerSong()
		{
			foreach (var (entryId, row) in drawerRows)
			{
				if (entryId == currentEntryId)
				{
					row.Classes.Add("accent");
				}
				else
				{
					row.Classes.Remove("accent");
				}
			}
		}

		private void SetSetlistHeader(string title, string? subtitle)
		{
			setlistTitle.Text = title;
			setlistSubtitle.Text = subtitle;
			setlistSubtitle.IsVisible = subtitle != null;
		}

		/// <summary>
		/// Keeps the setlist drawer in step with the page. When only the song changed within the
		/// setlist it shows, just the highlight moves; otherwise an open drawer reloads its songs
		/// (<paramref name="reload"/> forces that, e.g. after songs were added). Either way an open
		/// drawer scrolls the current song to its middle.
		/// </summary>
		private void RefreshSetlistDrawer(bool reload = false)
		{
			if (!reload && drawerSetlistId != null && drawerSetlistId == currentSetlist?.Id)
			{
				HighlightDrawerSong();
				// The current song may have changed: bring it to the middle of an open drawer.
				if (setlistDrawer.IsPaneOpen)
				{
					CenterDrawerSong();
				}

				return;
			}

			if (setlistDrawer.IsPaneOpen)
			{
				_ = LoadSetlistDrawerAsync(centerSong: true);
			}
			else
			{
				// Shown afresh the next time the drawer opens.
				drawerSetlistId = null;
				drawerRows.Clear();
			}
		}

		/// <summary>One song in the right drawer: its name, then key, tempo and beat; the current song is highlighted.</summary>
		private Button SongRow(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			var details = PageLayout.SongInfo(entry.Song);
			var content = new StackPanel { Spacing = 2 };
			content.Children.Add(new TextBlock { Text = entry.Song.Name, FontSize = 18, TextTrimming = TextTrimming.CharacterEllipsis });
			if (details.Length > 0)
			{
				content.Children.Add(new TextBlock { Text = details, FontSize = 13, Opacity = 0.75 });
			}

			var row = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(12, 8),
			};
			if (entry.Id == currentEntryId)
			{
				row.Classes.Add("accent");
			}

			row.Click += (_, _) =>
			{
				if (RefuseWhilePlaying())
				{
					return;
				}

				CloseUnlessPinned(setlistDrawer);
				currentEntryId = entry.Id;
				HighlightDrawerSong();
				SelectSong(group, setlist, entry);
			};
			return row;
		}

		private static TextBlock DrawerMessage(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

		private static Button DrawerItem(string text)
		{
			return new Button
			{
				Content = text,
				FontSize = 17,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(12, 10),
			};
		}

		/// <summary>
		/// A button showing a stroked icon. Icons are drawn as shapes because the browser has no system
		/// fonts to supply symbol glyphs such as ☰ or ✕.
		/// </summary>
		internal static Button IconButton(string pathData, string description)
		{
			var icon = new Path
			{
				Data = Geometry.Parse(pathData),
				StrokeThickness = 2,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 24,
				Height = 24,
				RenderTransformOrigin = RelativePoint.Center,
			};
			icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable(LyriousTheme.ForegroundBrush));

			var button = new Button { Content = icon, Padding = new Thickness(8), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
			ToolTip.SetTip(button, description);
			Avalonia.Automation.AutomationProperties.SetName(button, description);
			return button;
		}

		private Task StartAsync() => RunAsync(() => OpenStartPageAsync());

		/// <summary>
		/// Opens the start page of the member's current group (after login, and after adding a band).
		/// With <paramref name="showPage"/> false only the drawer follows the group (band, setlist and song);
		/// the page stays as it is (choosing another band in the band list).
		/// </summary>
		private async Task OpenStartPageAsync(bool showPage = true)
		{
			// A band's start page begins a new history: ✕ never leads back into another band's pages.
			history.Clear();
			var start = await StartPage.DetermineAsync(service);
			groupName.Text = start.Group?.Name ?? "Choose a band";
			currentGroup = start.Group;
			currentSetlist = null;
			SetCurrentSong(null, null);
			currentSetlistName.Text = "Choose a setlist";
			addSongsButton.IsEnabled = false;
			chooseSetlistButton.IsEnabled = start.Group != null;
			chooseSongButton.IsEnabled = start.Group != null;
			findSongButton.IsEnabled = start.Group != null;
			playlogButton.IsEnabled = start.Group != null;

			if (!showPage)
			{
				if (start.Setlist is { } setlist)
				{
					SetCurrentSetlist(setlist);
				}

				if (start.Kind == StartPageKind.Lyrics)
				{
					SetCurrentSong(start.Entry!.Song, start.Entry.Id);
				}
			}
			else switch (start.Kind)
			{
				case StartPageKind.Lyrics:
					ShowLyrics(start.Group!, start.Setlist!, start.Entry!);
					break;
				case StartPageKind.Setlist:
					await ShowSetlistAsync(start.Group!, start.Setlist!);
					break;
				case StartPageKind.Setlists:
					ShowSetlists(start.Group!, start.Setlists!);
					break;
				default:
					// Offline with no copy on this device yet, nothing can be shown: say why.
					ShowMessage(service.Status is { Online: false, LastSynced: null }
						? "You're offline, and this device has no copy of your bands yet. Connect to the server once to sync, then the app works offline too."
						: "You're not a member of a band yet. Ask a band admin to invite you.");
					break;
			}

			RefreshSetlistDrawer(reload: true);
		}

		/// <summary>The member's bands with their members, to choose the one to work with.</summary>
		private async Task ShowGroupsAsync()
		{
			var groups = await service.GetGroupsAsync();
			var members = await service.GetGroupMembersAsync();
			var view = new GroupsView(groups, members, currentGroup?.Id);
			// Choosing a band changes the current one but stays in the band list, now with that band highlighted.
			view.GroupSelected += group => _ = RunAsync(async () =>
			{
				await service.SelectGroupAsync(group.Id);
				await OpenStartPageAsync(showPage: false);
				await ShowGroupsAsync();
			});
			// A new band becomes the current one straight away.
			view.GroupCreateRequested += name => _ = RunAsync(async () =>
			{
				var group = await service.CreateGroupAsync(name);
				await service.SelectGroupAsync(group.Id);
				await OpenStartPageAsync();
			});
			page.Content = view;
		}

		private void ShowSetlists(GroupSummary group, IReadOnlyList<SetlistSummary> setlists)
		{
			var view = new SetlistsView(group, setlists, currentSetlist?.Id);
			view.SetlistSelected += setlist => _ = RunAsync(async () =>
			{
				if (bandSync)
				{
					await service.SelectSetlistAsync(group.Id, setlist.Id);
				}

				SetCurrentSong(null, null);
				await ShowSetlistAsync(group, setlist);
			});
			view.SetlistDeleteRequested += setlist => _ = RunAsync(async () =>
			{
				await service.DeleteSetlistAsync(setlist.Id);
				if (setlist.Id == currentSetlist?.Id)
				{
					// Nothing is current any more; the start page picks another setlist next time.
					currentSetlist = null;
					currentSetlistName.Text = "Choose a setlist";
					addSongsButton.IsEnabled = false;
					SetCurrentSong(null, null);
					playbackEntries = [];
					RefreshSetlistDrawer(reload: true);
				}

				ShowSetlists(group, await service.GetSetlistsAsync(group.Id));
			});
			view.SetlistCreateRequested += name => _ = RunAsync(async () =>
			{
				await service.CreateSetlistAsync(group.Id, name);
				ShowSetlists(group, await service.GetSetlistsAsync(group.Id));
			});
			page.Content = view;
		}

		private async Task ShowSetlistAsync(GroupSummary group, SetlistSummary setlist)
		{
			var entries = await service.GetEntriesAsync(setlist.Id);
			SetCurrentSetlist(setlist);
			playbackEntries = entries;

			var view = new SetlistView(setlist, entries);
			view.SongSelected += entry => SelectSong(group, setlist, entry);
			view.AddSongsRequested += () => _ = RunAsync(async () =>
				ShowAddSongs(group, setlist, await service.GetSongbookSongsAsync(group.Id), entries.Select(entry => entry.Song.Id).ToHashSet()));
			page.Content = view;
			RefreshSetlistDrawer(reload: true);
		}

		/// <summary>The songbook, to pick songs to add to the setlist; the setlist is shown again after adding.</summary>
		private void ShowAddSongs(GroupSummary group, SetlistSummary setlist, IReadOnlyList<SongDetails> songbook, IReadOnlySet<Guid> songsInSetlist)
		{
			var view = new AddSongsView(setlist, songbook, songsInSetlist);
			view.SongsAdded += songs => _ = RunAsync(async () =>
			{
				await service.AddSongsAsync(setlist.Id, songs.Select(song => song.Id).ToArray());
				await ShowSetlistAsync(group, setlist);
			});
			view.InternetSearchRequested += () => ShowInternetSearch(group, songbook, setlist);
			page.Content = view;
		}

		private void ShowLyrics(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			SetCurrentSetlist(setlist);
			SetCurrentSong(entry.Song, entry.Id);

			var view = new LyricScrollView(entry.Song, lyrics => service.UpdateLyricsAsync(entry.Song.Id, lyrics));
			ShowLyricsPage(view);
			RefreshSetlistDrawer();
		}

		/// <summary>
		/// The band's songbook, to choose a song. A song that is in the current setlist opens from
		/// there (and becomes the group's current song); any other song just shows its lyrics.
		/// </summary>
		private void ShowSongs(GroupSummary group, IReadOnlyList<SongDetails> songs)
		{
			var view = new SongsView(group, songs, currentSongId);
			view.SongSelected += song => _ = RunAsync(async () =>
			{
				if (currentSetlist is { } setlist
				    && (await service.GetEntriesAsync(setlist.Id)).FirstOrDefault(entry => entry.Song.Id == song.Id) is { } entry)
				{
					SelectSong(group, setlist, entry);
				}
				else
				{
					ShowSongbookLyrics(group, song);
				}
			});
			view.EmptySongRequested += name => _ = RunAsync(async () =>
				ShowSongbookLyrics(group, await service.CreateSongAsync(group.Id, name, "", TimeSpan.Zero), startEditing: true));
			view.SongImported += (name, lyrics) => _ = RunAsync(async () =>
				ShowSongbookLyrics(group, await service.CreateSongAsync(group.Id, name, lyrics, TimeSpan.Zero)));
			view.InternetSearchRequested += () => _ = RunAsync(async () => ShowInternetSearch(group, await service.GetSongbookSongsAsync(group.Id)));
			view.SongEditRequested += song => ShowSongEditor(group, song);
			view.SongDeleteRequested += song => _ = RunAsync(async () =>
			{
				await service.DeleteSongAsync(song.Id);
				if (song.Id == currentSongId)
				{
					SetCurrentSong(null, null);
				}

				// The setlist may have lost a song too.
				playbackEntries = [];
				RefreshSetlistDrawer(reload: true);
				ShowSongs(group, await service.GetSongbookSongsAsync(group.Id));
			});
			page.Content = view;
		}

		/// <summary>Edits a song's name, key, tempo, beat and lyrics; back to the songbook afterwards.</summary>
		private void ShowSongEditor(GroupSummary group, SongDetails song)
		{
			var view = new SongEditView(song, edited => service.UpdateSongAsync(edited));
			view.Cancelled += () => _ = RunAsync(async () => ShowSongs(group, await service.GetSongbookSongsAsync(group.Id)));
			view.Saved += saved => _ = RunAsync(async () =>
			{
				if (saved.Id == currentSongId)
				{
					SetCurrentSong(saved, currentEntryId);
				}

				// Setlists show the song's name, key, tempo and beat too.
				playbackEntries = [];
				RefreshSetlistDrawer(reload: true);
				ShowSongs(group, await service.GetSongbookSongsAsync(group.Id));
			});
			page.Content = view;
		}

		private void OpenInternetSearch()
		{
			if (currentGroup is { } group)
			{
				CloseUnlessPinned(menuDrawer);
				_ = RunAsync(async () => ShowInternetSearch(group, await service.GetSongbookSongsAsync(group.Id)));
			}
		}

		/// <summary>Searches for a song online; one that's added goes into the songbook and its lyrics are shown.</summary>
		/// <param name="addToSetlist">
		/// When searching from a setlist's "Add songs": the song found also goes at the end of this
		/// setlist, which is shown afterwards. Otherwise the new song's lyrics are shown.
		/// </param>
		private void ShowInternetSearch(GroupSummary group, IReadOnlyList<SongDetails> songbook, SetlistSummary? addToSetlist = null)
		{
			var names = songbook.Select(song => song.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
			var addText = addToSetlist == null ? "Add to songbook" : $"Add to {addToSetlist.Name}";
			var view = new InternetSongView(query => service.SearchInternetAsync(query), names, addText);
			view.AddRequested += found => _ = RunAsync(async () =>
			{
				var song = await service.CreateSongAsync(group.Id, found.Title, found.Lyrics, found.Duration);
				if (addToSetlist == null)
				{
					ShowSongbookLyrics(group, song);
					return;
				}

				await AddToSetlistAndShowAsync(group, addToSetlist, song);
			});
			page.Content = view;
		}

		/// <summary>The lyrics of a song that isn't in the current setlist; back goes to the songbook.</summary>
		/// <param name="startEditing">Opens the lyrics editor straight away (for a new empty song).</param>
		private void ShowSongbookLyrics(GroupSummary group, SongDetails song, bool startEditing = false)
		{
			SetCurrentSong(song, null);

			var view = new LyricScrollView(song, lyrics => service.UpdateLyricsAsync(song.Id, lyrics), startEditing);
			ShowLyricsPage(view);
			RefreshSetlistDrawer();
		}

		private void ShowLyricsPage(LyricScrollView view)
		{
			view.FocusModeToggled += () => SetFocusMode(!focusMode);
			view.SetFocusMode(focusMode);
			// Playing turns focus mode on, stopping (pause, or editing) turns it off.
			// A page that's been replaced (e.g. by the next song) no longer counts.
			view.AutoScrollChanged += () =>
			{
				// Stopped (also by leaving the page): the time played goes into the playlog.
				if (!view.IsAutoScrolling && timedPlay is { } timed && timed.View == view)
				{
					timedPlay = null;
					_ = AddPlayTimeAsync(timed.Play, DateTime.Now - timed.Started);
				}

				if (page.Content == view)
				{
					playbackBar.SetPlaying(view.IsAutoScrolling);
					playbackBar.SetNextAllowed(!view.IsAutoScrolling);
					SetFocusMode(view.IsAutoScrolling);

					// The band changed song while this was playing: follow now that it has stopped.
					if (!view.IsAutoScrolling && pendingFollow is { } follow)
					{
						pendingFollow = null;
						var catchUp = pendingCatchUp;
						pendingCatchUp = false;
						_ = FollowGroupAsync(follow, catchUp);
					}
				}
			};
			playbackBar.SetPlaying(false);
			playbackBar.SetNextAllowed(true);
			page.Content = view;
			_ = UpdatePlaybackNavigationAsync();
		}

		/// <summary>
		/// Loads the current setlist's songs, to show the next song's name in the playback toolbar
		/// where there's one after the current song. A song shown from the songbook (not from a
		/// setlist) has none.
		/// </summary>
		private async Task UpdatePlaybackNavigationAsync()
		{
			var setlist = currentSetlist;
			var entryId = currentEntryId;
			if (setlist == null || entryId == null)
			{
				playbackEntries = [];
				playbackBar.SetNextSong(null);
				return;
			}

			try
			{
				var entries = playbackEntries.Count > 0 && playbackEntries[0].SetlistId == setlist.Id
					? playbackEntries
					: await service.GetEntriesAsync(setlist.Id);
				if (currentEntryId != entryId)
				{
					return; // Another song was chosen meanwhile.
				}

				playbackEntries = entries;
				var index = IndexOfEntry(entries, entryId.Value);
				var nextSong = index >= 0 && index < entries.Count - 1 ? entries[index + 1].Song : null;
				var isLast = index >= 0 && index == entries.Count - 1;
				playbackBar.SetNextSong(nextSong?.Name, nextSong == null ? null : PageLayout.SongInfo(nextSong), lastInSetlist: isLast);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't load the setlist for the next song: {ex.Message}");
				playbackBar.SetNextSong(null);
			}
		}

		private static int IndexOfEntry(IReadOnlyList<SetlistEntry> entries, Guid entryId)
		{
			for (var i = 0; i < entries.Count; i++)
			{
				if (entries[i].Id == entryId)
				{
					return i;
				}
			}

			return -1;
		}

		/// <summary>Shows the next song in the current setlist.</summary>
		private void NextSong() => StepSong(1);

		/// <summary>Goes to the song before (-1) or after (+1) the current one in the setlist, if any (not while playing).</summary>
		private void StepSong(int step)
		{
			if (currentGroup is not { } group || currentSetlist is not { } setlist || currentEntryId is not { } entryId)
			{
				return;
			}

			var current = IndexOfEntry(playbackEntries, entryId);
			var target = current + step;
			if (current >= 0 && target >= 0 && target < playbackEntries.Count && !RefuseWhilePlaying())
			{
				SelectSong(group, setlist, playbackEntries[target]);
			}
		}

		/// <summary>
		/// Focus mode on: hides the drawers (keeping them pinned if they were) and the buttons that
		/// open them, and leaves only the song's name, details and lyrics on the page. Off: brings
		/// them back, reopening the drawers that were open.
		/// </summary>
		private void SetFocusMode(bool on)
		{
			if (on == focusMode)
			{
				return;
			}

			focusMode = on;
			if (on)
			{
				menuHiding = HideDrawer(menuDrawer);
				setlistHiding = HideDrawer(setlistDrawer);
				topRow.IsVisible = false; // It floats over the page, so hiding it moves nothing.
				Focus(); // So Escape reaches this view.
			}
			else
			{
				topRow.IsVisible = true;
				ShowDrawer(menuDrawer, menuHiding);
				ShowDrawer(setlistDrawer, setlistHiding);
			}

			if (page.Content is LyricScrollView lyrics)
			{
				lyrics.SetFocusMode(on);
			}
		}

		/// <summary>
		/// Hides a drawer for focus mode without moving the page. A drawer sliding over the page is
		/// closed. A pinned, open drawer stays open but is made invisible: closing it would widen the
		/// page and make the lyrics move and rewrap.
		/// </summary>
		private static DrawerHiding HideDrawer(SplitView drawer)
		{
			if (!drawer.IsPaneOpen)
			{
				return DrawerHiding.None;
			}

			if (!IsPinned(drawer))
			{
				drawer.IsPaneOpen = false;
				return DrawerHiding.Closed;
			}

			if (drawer.Pane is Control pane)
			{
				pane.IsVisible = false;
			}

			drawer.PaneBackground = Brushes.Transparent;
			return DrawerHiding.Invisible;
		}

		private static void ShowDrawer(SplitView drawer, DrawerHiding hiding)
		{
			switch (hiding)
			{
				case DrawerHiding.Closed:
					drawer.IsPaneOpen = true;
					break;
				case DrawerHiding.Invisible:
					if (drawer.Pane is Control pane)
					{
						pane.IsVisible = true;
					}

					drawer.Bind(SplitView.PaneBackgroundProperty, drawer.GetResourceObservable(LyriousTheme.CardBrush));
					break;
			}
		}

		/// <summary>The settings page; its ✕ goes back to the page before.</summary>
		private void ShowSettings() => page.Content = new SettingsView();

		/// <summary>
		/// Back to the page shown before the current one. Lyrics of a song that's no longer the current
		/// one aren't shown again: the current song's lyrics are opened instead. With nothing to go
		/// back to, the current song's lyrics (if any) are shown.
		/// </summary>
		private void GoBack()
		{
			while (history.Count > 0)
			{
				var previous = history[^1];
				history.RemoveAt(history.Count - 1);
				if (ReferenceEquals(previous, page.Content))
				{
					continue;
				}

				if (previous is LyricScrollView lyrics && lyrics.SongId != currentSongId)
				{
					break; // Out of date: open the current song instead (below).
				}

				goingBack = true;
				try
				{
					page.Content = previous;
				}
				finally
				{
					goingBack = false;
				}

				if (previous is LyricScrollView)
				{
					_ = UpdatePlaybackNavigationAsync();
				}

				return;
			}

			_ = RunAsync(ReopenCurrentSongAsync);
		}

		/// <summary>Opens the current song's lyrics, if a song from a setlist is current.</summary>
		private async Task ReopenCurrentSongAsync()
		{
			if (currentGroup is { } group && currentSetlist is { } setlist && currentEntryId is { } entryId
			    && await service.GetEntryAsync(entryId) is { } entry)
			{
				ShowLyrics(group, setlist, entry);
			}
			else
			{
				await OpenStartPageAsync();
			}
		}

		/// <summary>Whether the lyrics are playing (scrolling by themselves): the song can't be changed then.</summary>
		private bool IsPlaying => page.Content is LyricScrollView { IsAutoScrolling: true };

		/// <summary>While playing, refuses a change of song (and says why). True when refused.</summary>
		private bool RefuseWhilePlaying()
		{
			if (!IsPlaying)
			{
				return false;
			}

			ShowNotice("Stop playing to change song.");
			return true;
		}

		/// <summary>Shows a short message over the bottom of the page for a few seconds.</summary>
		private void ShowNotice(string text)
		{
			noticeText.Text = text;
			notice.IsVisible = true;
			var version = ++noticeVersion;
			DispatcherTimer.RunOnce(() =>
			{
				if (version == noticeVersion)
				{
					notice.IsVisible = false;
				}
			}, TimeSpan.FromSeconds(3));
		}

		/// <summary>
		/// Shows the song's lyrics straight away and saves it as the group's current song in the
		/// background, so the page doesn't flash a loading indicator between two songs. If saving
		/// fails the lyrics stay up; only the resume point for the next login is missed.
		/// </summary>
		private void SelectSong(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			if (RefuseWhilePlaying())
			{
				return;
			}

			ShowLyrics(group, setlist, entry);
			_ = SaveSongSelectionAsync(group, entry);
		}

		private async Task SaveSongSelectionAsync(GroupSummary group, SetlistEntry entry)
		{
			if (!bandSync)
			{
				return; // Band sync off: the band's current song stays as it is.
			}

			try
			{
				await service.SelectSongAsync(group.Id, entry);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't save the current song: {ex.Message}");
			}
		}

		/// <summary>How long an action may run before the loading indicator replaces the page.</summary>
		private static readonly TimeSpan LoadingDelay = TimeSpan.FromMilliseconds(300);

		/// <summary>
		/// Runs the action, with a loading indicator if it takes longer than <see cref="LoadingDelay"/>
		/// (so quick ones don't flash it), and shows the error (with a retry) if it fails.
		/// </summary>
		private async Task RunAsync(Func<Task> action)
		{
			var previous = page.Content;
			try
			{
				var task = action();
				if (await Task.WhenAny(task, Task.Delay(LoadingDelay)) != task && page.Content == previous)
				{
					page.Content = new ProgressBar { IsIndeterminate = true, Width = 200, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
				}

				await task;
			}
			catch (Exception ex)
			{
				var retry = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Center };
				retry.Click += (_, _) => _ = previous == null ? StartAsync() : RunAsync(action);
				ShowMessage($"Something went wrong. {ex.Message}", retry);
			}
		}

		private void ShowMessage(string message, Control? action = null)
		{
			var panel = new StackPanel { Spacing = 16, MaxWidth = 480, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
			if (action != null)
			{
				panel.Children.Add(action);
			}

			page.Content = panel;
		}
	}
}
