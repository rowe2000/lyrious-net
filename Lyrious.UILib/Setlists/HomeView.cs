using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lyrious.UILib.Login;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;
using ToggleButton = Avalonia.Controls.Primitives.ToggleButton;
using Lyrious.UILib.Localization;

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
		private static string NoSong => L.T("Choose a song");

		/// <summary>A page's room at the sides, clear of the drawer buttons over the top corners (4 of margin plus 40 wide).</summary>
		private static readonly Thickness PageMargin = new(44, 0);

		private const string AddIcon = "M12,5 V19 M5,12 H19";
		private const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";
		// A magnifying glass: the lens and its handle.
		private const string SearchIcon = "M4,10.5 A6.5,6.5 0 1 0 17,10.5 A6.5,6.5 0 1 0 4,10.5 M15.2,15.2 L20,20";
		// A pushpin: head, body widening to a base, and the needle.
		private const string PinIcon = "M9,3 H15 M10,3 V9 L7,13 H17 L14,9 V3 M12,13 V21";

		private readonly ISetlistService service;
		private readonly TextBlock groupName = new() { Text = L.T("Choose a band"), FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly TextBlock setlistTitle = new() { Text = L.T("Setlist"), FontSize = 20, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
		private readonly ContentControl setlistDrawerBody = new();
		private readonly TextBlock currentSetlistName = new() { Text = L.T("Choose a setlist"), FontSize = 18, FontWeight = FontWeight.Normal, FontStyle = FontStyle.Italic, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly TextBlock currentSongName = new() { Text = NoSong, FontSize = 18, FontWeight = FontWeight.Normal, FontStyle = FontStyle.Italic, TextTrimming = TextTrimming.CharacterEllipsis };
		private readonly Button chooseSetlistButton;
		private readonly Button chooseSongButton;
		private readonly Button findSongButton;
		private readonly Button playlogButton;
		private readonly Button chooseGroupButton;
		private readonly TextBlock syncStatus = new() { FontSize = 13, Opacity = 0.7 };
		// In the Sync item while the app is shared on the local network: at its right a QR code of
		// the address for the other devices, and under the sync status the address itself (see
		// ShowSharingCode).
		private readonly ContentControl sharingCode = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, IsVisible = false };
		private readonly TextBlock sharingAddress = new() { FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, IsVisible = false };
		private string? sharingCodeAddress;
		// The left drawer's Sync button: highlighted while changes made here haven't reached the server.
		private Button? syncButton;
		private readonly Button addSongsButton = IconButton(AddIcon, L.T("Add a song to the setlist"));
		// The right drawer's search: the box takes the place of the setlist's name and the tools, and
		// only the songs with the text in their name are listed.
		private readonly TextBox setlistSearchBox = PageLayout.WithClearButton(new TextBox { PlaceholderText = L.T("Search songs and tags"), VerticalAlignment = VerticalAlignment.Center, IsVisible = false });
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

		// While searching the right drawer: the tags of its songs to choose from, and the chosen ones
		// (a song is listed when it has all of them).
		private readonly WrapPanel drawerTagFilter = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(8, 0, 0, 8), IsVisible = false };
		private readonly HashSet<string> drawerChosenTags = new(StringComparer.CurrentCultureIgnoreCase);
		// And those longclicked: a song is listed when it has none of them.
		private readonly HashSet<string> drawerWithoutTags = new(StringComparer.CurrentCultureIgnoreCase);
		private int drawerLoad;

		/// <summary>Raised with the new password once the user has changed it on the profile page.</summary>
		public event Action<string>? PasswordChanged;

		/// <summary>Raised when the user wants to see or change the server (host) the app syncs with.</summary>
		public event Action? SyncRequested;

		/// <summary>Raised when the user wants to log out.</summary>
		public event Action? LogOutRequested;

		/// <summary>Raised when the user wants the password saved on this device removed.</summary>
		public event Action? ForgetSavedPasswordRequested;

		private readonly AuthenticatedUser user;
		// Offered on the profile page until used.
		private bool offerForgetSavedPassword;
		// The member's name, under "Profile" in the left drawer; follows a change of name on the profile page.
		private readonly TextBlock memberName = new() { FontSize = 13, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };

		/// <param name="offerForgetSavedPassword">Shows "Forget saved password" on the profile page (when a password is saved and the app can remove it).</param>
		/// <param name="skipOnboarding">Leaves out the screen of what's new (the view is made anew, e.g. in another language).</param>
		public HomeView(AuthenticatedUser user, ISetlistService service, bool offerForgetSavedPassword = false, bool skipOnboarding = false)
		{
			this.service = service;
			this.user = user;
			this.skipOnboarding = skipOnboarding;
			this.offerForgetSavedPassword = offerForgetSavedPassword;
			memberName.Text = user.DisplayName;
			// Sound files added here while offline go to the server once it can be reached.
			SoundSync.Watch(service);
			chooseSetlistButton = DrawerPicker(L.T("Setlist"), currentSetlistName, L.T("Choose a setlist"), () =>
			{
				if (currentGroup is { } group)
				{
					_ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
				}
			});
			chooseSetlistButton.IsEnabled = false;
			chooseSongButton = DrawerPicker(L.T("Songbook"), currentSongName, L.T("Choose a song from the songbook"), () =>
			{
				if (currentGroup is { } group)
				{
					_ = RunAsync(() => ShowSongsAsync(group));
				}
			});
			chooseSongButton.IsEnabled = false;
			findSongButton = DrawerItem(L.T("Find song online"));
			findSongButton.IsEnabled = false;
			findSongButton.Click += (_, _) => OpenInternetSearch();
			playlogButton = DrawerItem(L.T("Playlog"));
			playlogButton.IsEnabled = false;
			playlogButton.Click += (_, _) =>
			{
				if (currentGroup is { } group)
				{
					OpenFromDrawer();
					_ = RunAsync(async () => page.Content = new PlaylogView(group, await service.GetPlaylogsAsync(group.Id)));
				}
			};
			chooseGroupButton = DrawerPicker(L.T("Band"), groupName, L.T("Choose a band"), () => _ = RunAsync(ShowGroupsAsync));

			var menuButton = IconButton(MenuIcon, L.T("Open menu"));
			menuButton.HorizontalAlignment = HorizontalAlignment.Left;

			var setlistButton = IconButton(ListIcon, L.T("Open setlist"));
			setlistButton.HorizontalAlignment = HorizontalAlignment.Right;

			// The drawer buttons float over the top corners instead of taking a row of their own, so
			// pages start at the same height with or without them (as in focus mode). The page keeps
			// clear of the corners at the sides, so narrow windows don't put text under the buttons.
			topRow = new Grid { Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Top, Children = { menuButton, setlistButton } };
			page.Margin = PageMargin;

			setlistDrawer = Drawer(SplitViewPanePlacement.Right, 280);
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
			// Over the pages: the media player, which plays on whichever page shows (see MediaPlayerLayer).
			var mediaPlayer = new MediaPlayerLayer();
			MediaPlayerLayer.Current = mediaPlayer;
			mediaPlayer.PlayingChanged += OnMediaPlayingChanged;
			setlistDrawer.Content = new Grid { Children = { new DockPanel { Children = { playbackBar, page } }, mediaPlayer, topRow, notice } };
			playbackBar.PlayPauseRequested += () => PlayPause();
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

			menuButton.Click += (_, _) =>
			{
				// In focus mode a pinned menu is still open but invisible: show it again.
				if (focusMode && menuHiding == DrawerHiding.Invisible)
				{
					ShowDrawer(menuDrawer, menuHiding);
					menuHiding = DrawerHiding.None;
					menuButton.IsVisible = false;
					return;
				}

				lastOpenedDrawer = menuDrawer;
				menuDrawer.IsPaneOpen = true;
			};
			setlistButton.Click += (_, _) =>
			{
				lastOpenedDrawer = setlistDrawer;
				setlistDrawer.IsPaneOpen = true;
				_ = LoadSetlistDrawerAsync(centerSong: true);
			};
			// Opened again some other way (e.g. after focus mode): the current song in the middle too.
			// (Not when it slides back in after a press during its slide away: the list stays where it was.)
			setlistDrawer.PaneOpened += (_, _) =>
			{
				if (keepDrawerScroll)
				{
					keepDrawerScroll = false;
					return;
				}

				CenterDrawerSong();
			};
			// The computer may have changed networks since.
			menuDrawer.PaneOpened += (_, _) => ShowSharingCode();
			// Remembered for Escape, which opens the other drawer (see OnLyricsEscape).
			// Only a drawer's own opening counts: the right drawer is inside the left one's content, so
			// its opening reaches the left drawer too (the event goes up through it).
			menuDrawer.PaneOpening += (_, e) =>
			{
				if (e.Source == menuDrawer)
				{
					lastOpenedDrawer = menuDrawer;
				}
			};
			setlistDrawer.PaneOpening += (_, e) =>
			{
				if (e.Source == setlistDrawer)
				{
					lastOpenedDrawer = setlistDrawer;
				}
			};

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
				// (Nor a song's editor opened by itself: it's done with once it's left.)
				// A page opened from the left drawer starts afresh: ✕ there goes back to the song, not to
				// the pages before it (loading indicators and messages on the way don't count).
				if (clearHistoryOnNextPage && e.NewValue is UserControl)
				{
					clearHistoryOnNextPage = false;
					history.Clear();
				}
				else if (!goingBack && e.OldValue is UserControl left && !ReferenceEquals(left, e.NewValue) && left is not LyricScrollView { EditorOnly: true })
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

				// (A song's editor opened by itself has nothing to play.)
				var isLyrics = e.NewValue is LyricScrollView { EditorOnly: false };
				// The lyrics (and their editor) use the whole width, under the drawer buttons; other pages keep clear of them.
				page.Margin = e.NewValue is LyricScrollView ? new Thickness(0) : PageMargin;
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
			service.MemberConnectionChanged += OnMemberConnectionChanged;
			service.MembersChanged += OnMembersChanged;
			service.StatusChanged += OnStatusChanged;
			service.JoinedGroupChanged += OnJoinedGroupChanged;
			service.MySongSettingChanged += OnMySongSettingChanged;
			// The user's colours, the same on all their devices.
			UserSettings.Attach(service, user.UserName);
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
				// Connected now: this member is there too.
				_ = ShowPresentMembersAsync();
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
			service.MemberConnectionChanged -= OnMemberConnectionChanged;
			service.MembersChanged -= OnMembersChanged;
			service.StatusChanged -= OnStatusChanged;
			service.JoinedGroupChanged -= OnJoinedGroupChanged;
			service.MySongSettingChanged -= OnMySongSettingChanged;
			UserSettings.Detach();
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
			=> content is AddSongsView or InternetSongView or LyricScrollView { IsEditing: true };

		private bool? wasOnline;

		private void OnStatusChanged() => Dispatcher.UIThread.Post(() =>
		{
			var status = service.Status;
			syncStatus.Text = SyncStatusText.Short(status);
			// Gone offline or back online: who's there is different.
			if (status.Online != wasOnline)
			{
				wasOnline = status.Online;
				_ = ShowPresentMembersAsync();
			}

			if (syncButton != null)
			{
				syncButton.Classes.Set("accent", status.PendingChanges > 0);
			}
		});

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
				ShowNotice(L.T("Band sync on; no setlist picked here to send"));
				return;
			}

			if (IsSongbookSetlist(setlist))
			{
				ShowNotice(L.T("Band sync on; the songbook shown as a setlist isn't saved, so it can't be sent"));
				return;
			}

			try
			{
				if (currentEntryId is { } entryId && await service.GetEntryAsync(entryId) is { } entry)
				{
					await service.SelectSongAsync(group.Id, entry);
					ShowNotice(L.T("Sent to the band: {0}", entry.Song.Name));
				}
				else
				{
					await service.SelectSetlistAsync(group.Id, setlist.Id);
					ShowNotice(L.T("Sent to the band: {0}", setlist.Name));
				}

				if (page.Content is LyricScrollView lyrics)
				{
					await SendPlaybackAsync(lyrics.IsAutoScrolling);
				}
			}
			catch (Exception ex)
			{
				ShowNotice(L.T("Couldn't send to the band: {0}", ex.Message));
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
			if (App.LanSharing is { } sharing)
			{
				sharing.Changed += OnSharingChanged;
			}

			ShowSharingCode();
			keyWindow?.AddHandler(KeyDownEvent, OnLyricsKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
			// Bubbling: after everything else had its go at the key (see OnLyricsEscape).
			keyWindow?.AddHandler(KeyDownEvent, OnLyricsEscape, Avalonia.Interactivity.RoutingStrategies.Bubble);
			keyWindow?.AddHandler(KeyDownEvent, OnLyricsPaste, Avalonia.Interactivity.RoutingStrategies.Bubble);
		}

		// The drawer opened last (by its button, by Escape or otherwise): Escape opens the other one.
		private SplitView? lastOpenedDrawer;

		/// <summary>
		/// Escape on the lyrics page, when nothing else used it (a close button on screen, see
		/// <see cref="EscapeKey"/>, a text box, ending focus mode): opens the drawer that wasn't
		/// opened last (the left one the first time), so Escape takes turns between them. There,
		/// Escape closes it again.
		/// </summary>
		private void OnLyricsEscape(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			if (e.Key != Avalonia.Input.Key.Escape || e.KeyModifiers != Avalonia.Input.KeyModifiers.None || e.Handled
			    || focusMode || page.Content is not LyricScrollView { IsEditing: false })
			{
				return;
			}

			var drawer = lastOpenedDrawer == menuDrawer ? setlistDrawer : menuDrawer;
			if (drawer.IsPaneOpen)
			{
				return; // Open already (e.g. pinned).
			}

			e.Handled = true;
			lastOpenedDrawer = drawer; // The next Escape opens the other one.
			drawer.IsPaneOpen = true;
			if (drawer == setlistDrawer)
			{
				// As its button does: the songs loaded, the current one in the middle.
				_ = LoadSetlistDrawerAsync(centerSong: true);
			}
		}

		/// <summary>
		/// Ctrl/⌘+V on the lyrics page (not while editing), when nothing else used it (a text box pastes
		/// its own): a link or sound files on the clipboard are added to the song's media links.
		/// </summary>
		private void OnLyricsPaste(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			var command = e.KeyModifiers == Avalonia.Input.KeyModifiers.Control || e.KeyModifiers == Avalonia.Input.KeyModifiers.Meta;
			if (e.Key == Avalonia.Input.Key.V && command && !e.Handled && page.Content is LyricScrollView { IsEditing: false } lyrics)
			{
				e.Handled = true;
				_ = lyrics.PasteMediaAsync();
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			keyWindow?.RemoveHandler(KeyDownEvent, OnLyricsKey);
			keyWindow?.RemoveHandler(KeyDownEvent, OnLyricsEscape);
			keyWindow?.RemoveHandler(KeyDownEvent, OnLyricsPaste);
			keyWindow = null;
			if (App.LanSharing is { } sharing)
			{
				sharing.Changed -= OnSharingChanged;
			}
		}

		// Raised on any thread.
		private void OnSharingChanged() => Dispatcher.UIThread.Post(ShowSharingCode);

		/// <summary>
		/// In the left drawer's Sync item, while the app is shared on the local network (the Sync
		/// page): a QR code to point another device's camera at, which opens the web app there
		/// through this computer, and the address below it for typing. With several networks it's
		/// the first one's address; the Sync page lists them all.
		/// </summary>
		private void ShowSharingCode()
		{
			var address = App.LanSharing is { IsRunning: true } sharing ? sharing.Addresses.FirstOrDefault() : null;
			if (address == sharingCodeAddress)
			{
				return;
			}

			sharingCodeAddress = address;
			sharingCode.Content = address != null ? QrCodeImage.Create(address, 104) : null;
			sharingCode.IsVisible = sharingAddress.IsVisible = address != null;
			sharingAddress.Text = address;
		}

		/// <summary>
		/// Keys on the lyrics page (not while editing or typing in a box): the space bar plays and stops,
		/// as the play/stop button does; up and down nudge the lyrics (4 bars for timed songs, else a
		/// line); Home, End, Page Up and Page Down go to the start, the end, and a page back and on;
		/// and, when not playing, left and right go to the previous and next song in the setlist.
		/// Handled before the focused control sees them, so a focused button isn't also pressed by the
		/// space bar (the play button would undo it).
		/// </summary>
		private void OnLyricsKey(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			var upOrDown = e.Key is Avalonia.Input.Key.Up or Avalonia.Input.Key.Down;

			// The open menu drawer (left) takes up and down first, on any page. The setlist drawer
			// (right) doesn't: there the keys stay with the lyrics.
			if (e.KeyModifiers == Avalonia.Input.KeyModifiers.None && upOrDown
			    && MoveInMenuDrawer(e.Key == Avalonia.Input.Key.Down ? 1 : -1))
			{
				e.Handled = true;
				return;
			}

			// Typing in a box keeps its keys, but for up and down in the setlist drawer's search box
			// (a single line, they do nothing there): those nudge the lyrics as well.
			if (e.KeyModifiers != Avalonia.Input.KeyModifiers.None || page.Content is not LyricScrollView { IsEditing: false } lyrics
			    || (e.Source is TextBox box && !(upOrDown && box == setlistSearchBox)))
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
				case Avalonia.Input.Key.Home:
					e.Handled = true;
					lyrics.ScrollHome();
					break;
				case Avalonia.Input.Key.End:
					e.Handled = true;
					lyrics.ScrollEnd();
					break;
				case Avalonia.Input.Key.PageUp:
					e.Handled = true;
					lyrics.ScrollPage(-1);
					break;
				case Avalonia.Input.Key.PageDown:
					e.Handled = true;
					lyrics.ScrollPage(1);
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

		/// <summary>
		/// Up and down in the open menu drawer (left): the keyboard focus moves to the item above or
		/// below (the drawer's buttons with text, not the icon buttons in its header), which Enter
		/// then presses, as for any focused button. The first press goes to the current item, else
		/// the first or last one. When it slides over the page the drawer takes the keys whenever
		/// it's open; when pinned only once the focus is in it, so the keys keep working on the page
		/// beside it. False when the drawer didn't take the key.
		/// </summary>
		private bool MoveInMenuDrawer(int direction)
		{
			var focused = keyWindow?.FocusManager?.GetFocusedElement() as Visual;
			if (!menuDrawer.IsPaneOpen || menuDrawer.Pane is not Visual activePane
			    || (IsPinned(menuDrawer) && (focused == null || !activePane.IsVisualAncestorOf(focused))))
			{
				return false;
			}

			var items = activePane.GetVisualDescendants().OfType<Button>()
				.Where(button => button.Content is not Path && button.Focusable && button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
				.ToList();
			if (items.Count == 0)
			{
				return false;
			}

			var at = focused is Button current ? items.IndexOf(current) : -1;
			Button next;
			if (at >= 0)
			{
				next = items[Math.Clamp(at + direction, 0, items.Count - 1)];
			}
			else
			{
				// Not on an item yet: the current one (highlighted), else the first or the last.
				next = items.FirstOrDefault(button => button.Classes.Contains("accent")) ?? (direction > 0 ? items[0] : items[^1]);
			}

			next.Focus(Avalonia.Input.NavigationMethod.Directional);
			next.BringIntoView();
			return true;
		}

		/// <summary>Play/pause on the lyrics page (the toolbar button, or the space bar).</summary>
		/// <summary>
		/// The media player started or stopped playing (play or pause pressed in it): the song's lyrics,
		/// if they're the page shown, start or stop scrolling with it, as with the play button (the
		/// band that's joined follows too). Stopping this way doesn't go on to the next song.
		/// </summary>
		private void OnMediaPlayingChanged(bool playing)
		{
			if (page.Content is not LyricScrollView { EditorOnly: false, IsEditing: false } lyrics
			    || lyrics.SongId != MediaPlayerLayer.Current?.SongId
			    || lyrics.IsAutoScrolling == playing)
			{
				return;
			}

			if (playing)
			{
				PlayPause();
			}
			else
			{
				lyrics.SetAutoScroll(false);
				_ = SendPlaybackAsync(false);
			}
		}

		/// <param name="goOnAtEnd">Stopping at the very end may go on to the next song (not when a media file ended by itself).</param>
		private void PlayPause(bool goOnAtEnd = true)
		{
			if (page.Content is LyricScrollView lyrics)
			{
				var stopping = lyrics.IsAutoScrolling;
				var atEnd = lyrics.IsAtEnd;
				lyrics.SetAutoScroll(!stopping);

				// The song's sound file (if it has one) starts and stops with it; the lyrics then follow it.
				if (lyrics.IsAutoScrolling)
				{
					_ = lyrics.StartMediaAsync();
				}
				else
				{
					lyrics.StopMedia();
				}

				// The other members that have joined the band start or stop too (sent first, before
				// the playlog is written).
				_ = SendPlaybackAsync(lyrics.IsAutoScrolling);
				if (lyrics.IsAutoScrolling)
				{
					StartPlay(lyrics);
				}

				// Stopping at the very end of the lyrics goes on to the next song (a setting, on by default).
				if (goOnAtEnd && stopping && atEnd && PlaybackSettings.NextSongOnStopAtEnd)
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

		/// <summary>A bandmate came online or went offline: the band page (if showing) shows it.</summary>
		private void OnMemberConnectionChanged(Guid memberId, bool connected) => Dispatcher.UIThread.Post(() =>
		{
			if (page.Content is GroupsView bands)
			{
				_ = RunAsync(() => ShowGroupsAsync(bands.SelectedGroupId, replace: true));
			}

			_ = ShowPresentMembersAsync();
		});

		/// <summary>A member or membership changed (a name, the band someone has joined): who's there may have too.</summary>
		private void OnMembersChanged() => Dispatcher.UIThread.Post(() => _ = ShowPresentMembersAsync());

		/// <summary>The member's own settings for a song changed on another of their devices: the lyrics showing it follow.</summary>
		private void OnMySongSettingChanged(Guid songId) => Dispatcher.UIThread.Post(async () =>
		{
			if (page.Content is LyricScrollView lyrics && lyrics.SongId == songId && !lyrics.IsEditing)
			{
				try
				{
					var setting = await service.GetMySongSettingAsync(songId);
					if (page.Content == lyrics && !lyrics.IsEditing)
					{
						lyrics.MySetting = setting;
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Couldn't read the member's own song settings: {ex.Message}");
				}
			}
		});

		/// <summary>Gives the lyrics page the member's own transpose and lyrics for the song.</summary>
		private void UseMySetting(LyricScrollView view, Guid groupId, Guid songId)
		{
			// The band's admins also see, change and add the other members' own lyrics.
			view.LoadMembersLyrics = () => service.GetMembersLyricsAsync(groupId, songId);
			view.SaveMemberLyrics = (memberId, lyrics) => service.SaveMemberLyricsAsync(songId, memberId, lyrics);

			view.UseMySetting(() => service.GetMySongSettingAsync(songId), setting => service.SaveMySongSettingAsync(songId, setting));
		}

		private int presentMembersLoad;

		/// <summary>
		/// Shows, in the playback toolbar, the initials of who's there: the current band's members that
		/// have joined it and are connected right now (this member too). Nobody while offline.
		/// </summary>
		private async Task ShowPresentMembersAsync()
		{
			var load = ++presentMembersLoad;
			IReadOnlyList<string> names = [];
			try
			{
				if (currentGroup is { } group && (await service.GetGroupMembersAsync()).TryGetValue(group.Id, out var members))
				{
					names = members.Where(member => member.Approved && member.Joined && member.Connected).Select(member => member.Name).ToArray();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't get who's there: {ex.Message}");
			}

			// A newer one is on its way.
			if (load == presentMembersLoad)
			{
				playbackBar.SetMembers(names);
			}
		}

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
				var replace = page.Content is SetlistsView or NoSongView or LyricScrollView { IsEditing: false };
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

				// Not a setlist being looked at: it stays on that setlist (the drawers follow the band).
				var canReplacePage = page.Content is SetlistsView or NoSongView or LyricScrollView { IsEditing: false };
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
					// The band's setlist, but no song in it yet: the page says a song is still to be chosen.
					SetCurrentSetlist(setlist);
					SetCurrentSong(null, null);
					playbackEntries = [];
					RefreshSetlistDrawer(reload: true);
					ShowNoSong();
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
		private static StackPanel DrawerButtons(SplitView drawer, Button openButton, string name, bool closeFirst = false)
		{
			var pinButton = IconButton(PinIcon, L.T("Pin {0}", name));
			var closeButton = EscapeKey.MarkAsClose(IconButton(CloseIcon, L.T("Close {0}", name)));
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

				var description = pinned ? L.T("Unpin {0}", name) : L.T("Pin {0}", name);
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

			// ✕ first in the left drawer (at its left edge, by the button that opens it), last in the right one.
			return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { closeFirst ? closeButton : pinButton, closeFirst ? pinButton : closeButton } };
		}

		// The next page shown was opened from the left drawer: the history of pages starts afresh.
		private bool clearHistoryOnNextPage;

		/// <summary>
		/// Shows a page the host made for a left drawer item (the Sync page) as the other pages are
		/// shown: between the drawers, starting a new history of pages.
		/// </summary>
		public void ShowDrawerPage(Control view)
		{
			clearHistoryOnNextPage = true;
			page.Content = view;
		}

		/// <summary>Leaves the page shown, as its ✕ does (back to the page before, or to the song).</summary>
		public void LeavePage() => GoBack();

		/// <summary>
		/// A left drawer item opens its page: the drawer closes (unless pinned), and the page starts a
		/// new history, so ✕ on it (and back from it) leads to the song, not to earlier pages.
		/// </summary>
		private void OpenFromDrawer()
		{
			CloseUnlessPinned(menuDrawer);
			clearHistoryOnNextPage = true;
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
		/// <summary>
		/// Puts an icon (see <see cref="AppIcons"/>) before a left drawer item's text (or its lines of
		/// text), in the item's text colour, so it follows the item's highlights.
		/// </summary>
		private static void WithDrawerIcon(Button item, string iconData)
		{
			var icon = AppIcons.Create(iconData, 20);
			icon.Margin = new Thickness(0, 0, 12, 0);
			icon.Opacity = 0.85;
			DockPanel.SetDock(icon, Dock.Left);
			Control content = item.Content as Control ?? new TextBlock { Text = item.Content?.ToString(), VerticalAlignment = VerticalAlignment.Center };
			item.Content = null;
			item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
			item.Content = new DockPanel { Children = { icon, content } };
		}

		private Button DrawerPicker(string caption, TextBlock current, string description, Action choose)
		{
			var button = new Button
			{
				Content = new StackPanel
				{
					Children = { new TextBlock { Text = caption, FontSize = 13, Opacity = 0.75 }, current },
				},
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(10, 5),
			};
			RoundedHighlight(button);
			ToolTip.SetTip(button, description);
			button.Click += (_, _) =>
			{
				OpenFromDrawer();
				choose();
			};
			return button;
		}

		/// <summary>The setlist's name on the left drawer's button in bold; with none, "Choose a setlist" in italics.</summary>
		private void ShowSetlistName(string? name)
		{
			currentSetlistName.Text = name ?? L.T("Choose a setlist");
			currentSetlistName.FontWeight = name == null ? FontWeight.Normal : FontWeight.SemiBold;
			currentSetlistName.FontStyle = name == null ? FontStyle.Italic : FontStyle.Normal;
		}

		/// <summary>Records the setlist the app is showing, and shows its name on the left drawer's button.</summary>
		private void SetCurrentSetlist(SetlistSummary setlist)
		{
			currentSetlist = setlist;
			ShowSetlistName(setlist.Name);
			// Songs can't be added to the songbook shown as a setlist: it isn't saved.
			addSongsButton.IsEnabled = !IsSongbookSetlist(setlist);
		}

		// The songbook's songs shown as the setlist (see ShowSongbookAsSetlist): only here, never saved.
		private IReadOnlyList<SetlistEntry> songbookEntries = [];

		/// <summary>
		/// Whether the setlist is the songbook shown as a setlist: one that isn't saved, known by its
		/// id, which is its band's (no saved setlist has that).
		/// </summary>
		private static bool IsSongbookSetlist(SetlistSummary setlist) => setlist.Id == setlist.GroupId;

		/// <summary>The setlist's songs: from the service, or those of the songbook shown as a setlist.</summary>
		private Task<IReadOnlyList<SetlistEntry>> EntriesAsync(SetlistSummary setlist)
			=> IsSongbookSetlist(setlist) ? Task.FromResult(songbookEntries) : service.GetEntriesAsync(setlist.Id);

		/// <summary>A setlist entry by its id, also one of the songbook shown as a setlist.</summary>
		private async Task<SetlistEntry?> EntryAsync(Guid entryId)
			=> songbookEntries.FirstOrDefault(entry => entry.Id == entryId) ?? await service.GetEntryAsync(entryId);

		/// <summary>
		/// Shows the songs as the setlist in the right drawer, just here: nothing is saved, and the
		/// band's setlist stays as it is. The current song stays current when it's among them; the
		/// next and previous song go through them in their order.
		/// </summary>
		private void ShowSongbookAsSetlist(GroupSummary group, IReadOnlyList<SongDetails> songs)
		{
			var setlist = new SetlistSummary(group.Id, L.T("Songbook · {0}", group.Name), group.Id);
			// Each song's entry has the song's id, so the current song is found in it.
			songbookEntries = songs.Select((song, index) => new SetlistEntry(song.Id, setlist.Id, index, song)).ToArray();
			SetCurrentSetlist(setlist);
			playbackEntries = songbookEntries;
			if (currentSongId is { } songId && songbookEntries.FirstOrDefault(entry => entry.Song.Id == songId) is { } current)
			{
				currentEntryId = current.Id;
			}
			else
			{
				currentEntryId = null;
			}

			_ = UpdatePlaybackNavigationAsync();
			RefreshSetlistDrawer(reload: true);
			setlistDrawer.IsPaneOpen = true;
			_ = LoadSetlistDrawerAsync(centerSong: true);
			ShowNotice(L.Plural(songs.Count, "The songbook is the setlist here ({0} song); it isn't saved", "The songbook is the setlist here ({0} songs); it isn't saved"));
		}

		/// <summary>Records the song the app is showing (and its setlist entry, if it's shown from a setlist), for the Song button and the highlights.</summary>
		private void SetCurrentSong(SongDetails? song, Guid? entryId)
		{
			// Another song: the media player stops (unless it plays this song's link).
			if (song != null)
			{
				MediaPlayerLayer.Current?.StopUnlessSong(song.Id);
			}

			currentSongId = song?.Id;
			currentEntryId = entryId;
			currentSongName.Text = song?.Name ?? NoSong;
			// "Choose a song" (no song) in italics; a song's name in bold.
			currentSongName.FontWeight = song == null ? FontWeight.Normal : FontWeight.SemiBold;
			currentSongName.FontStyle = song == null ? FontStyle.Italic : FontStyle.Normal;
		}

		/// <summary>The left drawer: member, band, setlist and song at the top, account actions at the bottom.</summary>
		private Control MenuDrawerContent(AuthenticatedUser user, Button openButton, bool offerForgetSavedPassword)
		{
			var buttons = DrawerButtons(menuDrawer, openButton, "menu", closeFirst: true);
			DockPanel.SetDock(buttons, Dock.Left);

			var settings = DrawerItem(L.T("Settings"));
			settings.Click += (_, _) =>
			{
				OpenFromDrawer();
				ShowSettings();
			};

			var sync = DrawerItem(L.T("Sync"));
			syncButton = sync;
			// Under "Sync": whether the server can be reached and how many changes are waiting.
			// At the right, level with "Sync": the QR code while the app is shared on the local network.
			DockPanel.SetDock(sharingCode, Dock.Right);
			sync.HorizontalContentAlignment = HorizontalAlignment.Stretch;
			sync.Content = new DockPanel
			{
				Children =
				{
					sharingCode,
					new StackPanel
					{
						Spacing = 2,
						Children = { new TextBlock { Text = L.T("Sync") }, syncStatus, sharingAddress },
					},
				},
			};
			sync.Click += (_, _) =>
			{
				// The host shows it (see ShowDrawerPage), as the other pages are shown.
				OpenFromDrawer();
				SyncRequested?.Invoke();
			};

			// The member's name, password, saved password and logging out; the name under "Profile".
			var profile = DrawerItem(L.T("Profile"));
			profile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
			profile.Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = L.T("Profile") }, memberName } };
			profile.Click += (_, _) =>
			{
				OpenFromDrawer();
				ShowProfile();
			};

			var header = new StackPanel
			{
				Spacing = 4,
				Children =
				{
					// The drawer's buttons (✕ and pin) at the left, 4px above the first item (the panel's spacing).
					new DockPanel { Children = { buttons } },
					chooseGroupButton,
					chooseSetlistButton,
					chooseSongButton,
					findSongButton,
					playlogButton,
				},
			};
			DockPanel.SetDock(header, Dock.Top);

			// The account actions sit at the bottom of the drawer.
			// Its top margin is the gap between items, kept when the two groups meet (a short drawer).
			var account = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0), Children = { settings, sync } };
			DockPanel.SetDock(account, Dock.Bottom);

			// Last: about the app (version, release notes), the profile, and on desktop quitting it.
			var about = DrawerItem(L.T("About"));
			// The version under "About", as the status is under "Sync".
			about.HorizontalContentAlignment = HorizontalAlignment.Stretch;
			about.Content = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new TextBlock { Text = L.T("About") },
					new TextBlock { Text = L.T("Version {0}", ReleaseNotes.AppVersion), FontSize = 13, Opacity = 0.7 },
				},
			};
			about.Click += (_, _) =>
			{
				OpenFromDrawer();
				page.Content = new AboutView();
			};
			account.Children.Add(about);
			account.Children.Add(profile);
			if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
			{
				var exit = DrawerItem(L.T("Exit"));
				exit.Click += (_, _) => desktop.Shutdown();
				WithDrawerIcon(exit, AppIcons.Exit);
				account.Children.Add(exit);
			}

			// The songs' sound files kept on this device, first of these (just above Settings).
			if (App.SoundStore is { } sounds)
			{
				var soundFiles = DrawerItem(L.T("Sound files"));
				soundFiles.Click += (_, _) =>
				{
					OpenFromDrawer();
					page.Content = new SoundFilesView(sounds, service);
				};
				WithDrawerIcon(soundFiles, AppIcons.SoundFiles);
				account.Children.Insert(0, soundFiles);
			}

			// An icon before each item's text.
			WithDrawerIcon(chooseGroupButton, AppIcons.Band);
			WithDrawerIcon(chooseSetlistButton, AppIcons.Setlist);
			WithDrawerIcon(chooseSongButton, AppIcons.Songbook);
			WithDrawerIcon(findSongButton, AppIcons.FindOnline);
			WithDrawerIcon(playlogButton, AppIcons.Playlog);
			WithDrawerIcon(settings, AppIcons.Settings);
			WithDrawerIcon(sync, AppIcons.Sync);
			WithDrawerIcon(about, AppIcons.About);
			WithDrawerIcon(profile, AppIcons.Profile);

			var panel = new DockPanel
			{
				Margin = new Thickness(4),
				LastChildFill = false,
				Children = { header, account },
			};

			// Scrolls when the items don't fit. At least as tall as the drawer, so the account actions
			// still sit at its bottom when everything fits.
			var scroller = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
			scroller.SizeChanged += (_, e) => panel.MinHeight = Math.Max(0, e.NewSize.Height - panel.Margin.Top - panel.Margin.Bottom);
			return scroller;
		}

		/// <summary>
		/// The "+" menu of ways to add a song to a setlist (the right drawer's, and the setlist
		/// page's); each puts the song at the end of the setlist <paramref name="target"/> gives
		/// when the item is clicked, after <paramref name="before"/> (e.g. closing the drawer).
		/// </summary>
		private MenuFlyout AddSongsMenu(Func<(GroupSummary Group, SetlistSummary Setlist)?> target, Action? before = null)
		{
			MenuItem Item(string header, Func<GroupSummary, SetlistSummary, Task> add)
			{
				var item = new MenuItem { Header = header };
				item.Click += (_, _) =>
				{
					if (target() is { } chosen)
					{
						before?.Invoke();
						_ = add(chosen.Group, chosen.Setlist);
					}
				};
				return item;
			}

			return new MenuFlyout
			{
				Items =
				{
					Item(L.T("From the songbook…"), (group, setlist) => RunAsync(async () =>
					{
						var entries = await service.GetEntriesAsync(setlist.Id);
						ShowAddSongs(group, setlist, await service.GetSongbookSongsAsync(group.Id), entries.Select(entry => entry.Song.Id).ToHashSet());
					})),
					Item(L.T("New empty song…"), (group, setlist) =>
					{
						ShowNewSongEditor(group, setlist);
						return Task.CompletedTask;
					}),
					Item(L.T("Import from file…"), ImportSongToSetlistAsync),
					Item(L.T("Find on the internet…"), (group, setlist) => RunAsync(async () =>
						ShowInternetSearch(group, await service.GetSongbookSongsAsync(group.Id), setlist))),
				},
			};
		}

		/// <summary>Fills in a new song (name, key, tempo, beat, lyrics); saving adds it to the songbook and the setlist.</summary>
		private void ShowNewSongEditor(GroupSummary group, SetlistSummary setlist)
		{
			var blank = new SongDetails(Guid.Empty, "", "", 120, "4/4", "");
			var view = new LyricScrollView(blank, async filled =>
			{
				var created = await service.CreateSongAsync(group.Id, filled.Name, filled.Lyrics, TimeSpan.Zero);
				return await service.UpdateSongAsync(filled with { Id = created.Id });
			}, startEditing: true) { EditorOnly = true };
			ShowMidiProblems(view, group.Id);
			view.EditCancelled += () => _ = RunAsync(() => ShowSetlistAsync(group, setlist));
			view.SongSaved += song => _ = RunAsync(() => AddToSetlistAndShowAsync(group, setlist, song));
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

		/// <summary>The right drawer: a fixed header (setlist name, "+", pin and ✕) and the song list.</summary>
		private Control SetlistDrawerContent(Button openButton)
		{
			var buttons = DrawerButtons(setlistDrawer, openButton, "setlist");
			DockPanel.SetDock(buttons, Dock.Right);

			// "+" in front of pin and ✕: add songs from the songbook, as on the setlist page.
			addSongsButton.IsEnabled = false;
			// "+" opens a menu of ways to add a song to the current setlist.
			addSongsButton.Flyout = AddSongsMenu(
				() => currentGroup is { } group && currentSetlist is { } setlist ? (group, setlist) : null,
				() => CloseUnlessPinned(setlistDrawer));
			buttons.Children.Insert(0, addSongsButton);

			// A click outside the drawer while the "+" menu is open only closes the menu: the drawer
			// stays. The press is seen here first (tunnelling); until it's let go, the drawer doesn't
			// close. Choosing an item in the menu (in its own popup) still closes the drawer as before.
			// The menu may close on the press before it gets here: "just closed" lasts until the
			// dispatcher is done with that press.
			var addMenuOpen = false;
			var addMenuJustClosed = false;
			var pressClosesMenu = false;
			addSongsButton.Flyout.Opened += (_, _) => addMenuOpen = true;
			addSongsButton.Flyout.Closed += (_, _) =>
			{
				addMenuOpen = false;
				addMenuJustClosed = true;
				Dispatcher.UIThread.Post(() => addMenuJustClosed = false, DispatcherPriority.Background);
			};
			AddHandler(PointerPressedEvent, (_, _) => pressClosesMenu = addMenuOpen || addMenuJustClosed,
				Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
			AddHandler(PointerReleasedEvent, (_, _) =>
			{
				if (pressClosesMenu)
				{
					// After the drawer's own handling of the release.
					Dispatcher.UIThread.Post(() => pressClosesMenu = false);
				}
			}, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
			setlistDrawer.PaneClosing += (_, e) =>
			{
				if (pressClosesMenu)
				{
					e.Cancel = true;
				}
			};

			// The magnifying glass, in front of the other tools: the search box takes the place of the
			// setlist's name and the tools, with only a ✕ beside it to end the search.
			var searchButton = IconButton(SearchIcon, L.T("Search the setlist"));
			buttons.Children.Insert(0, searchButton);
			// While the search shows, Escape only ends it: nothing else closes (see the box's KeyDown too).
			var endSearchButton = EscapeKey.MarkAsClose(IconButton(CloseIcon, L.T("End the search")), before: true);
			endSearchButton.IsVisible = false;

			void ShowSearch(bool searching)
			{
				if (setlistSearchBox.IsVisible == searching)
				{
					return;
				}

				setlistSearchBox.IsVisible = searching;
				endSearchButton.IsVisible = searching;
				setlistTitle.IsVisible = !searching;
				buttons.IsVisible = !searching;
				// The songs' tags to choose from, while searching (when they have any).
				drawerTagFilter.IsVisible = searching && drawerTagFilter.Children.Count > 0;
				if (searching)
				{
					// Once it's on screen.
					Dispatcher.UIThread.Post(() => setlistSearchBox.Focus(), DispatcherPriority.Background);
				}
				else
				{
					// Every song is listed again.
					drawerChosenTags.Clear();
					drawerWithoutTags.Clear();
					ShowDrawerTagButtons();

					setlistSearchBox.Text = string.Empty;
					FilterDrawerSongs();
					CenterDrawerSong();
				}
			}

			searchButton.Click += (_, _) => ShowSearch(true);
			endSearchButton.Click += (_, _) => ShowSearch(false);
			setlistSearchBox.TextChanged += (_, _) => FilterDrawerSongs();
			// Enter opens the first song found. Escape ends the search, here already: on its way up it
			// would close a drawer that slides over the page (the drawer's own Escape).
			setlistSearchBox.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Escape && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
				{
					e.Handled = true;
					ShowSearch(false);
					return;
				}

				if (e.Key == Avalonia.Input.Key.Enter && FirstDrawerSongShown() is { } row)
				{
					e.Handled = true;
					row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
				}
			};
			drawerStayTimer.Tick += (_, _) => SlideSetlistDrawerAway();
			// Tunnelling, so it's seen before the drawer's own closing (when the press is let go).
			AddHandler(PointerPressedEvent, OnPressWhileDrawerLingers, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
			// The page scrolls with the mouse wheel or touchpad also while a drawer slides over it.
			AddHandler(PointerWheelChangedEvent, OnWheelBesideOpenDrawer, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
			// (Not when it slides back in after a press during its slide away: that keeps its own slide.)
			setlistDrawer.PaneOpening += (_, _) =>
			{
				if (!drawerSlidingBack)
				{
					EndSlowDrawerSlide();
				}
			};
			// Closed some other way while it waited to slide away: nothing left to do.
			setlistDrawer.PaneClosed += (_, _) => drawerStayTimer.Stop();
			// A drawer that slid away opens without a search the next time.
			setlistDrawer.PaneClosed += (_, _) => ShowSearch(false);

			// A name cut short (…) is shown whole while hovered with a mouse or pen, or after a tap on a
			// touch screen until the next tap: the tools but ✕ step aside for it.
			var hiddenForTitle = new List<Control>();

			void ShowWholeTitle()
			{
				if (hiddenForTitle.Count > 0 || setlistTitle.TextLayout?.TextLines.Any(line => line.HasCollapsed) != true)
				{
					return;
				}

				foreach (var tool in buttons.Children.Take(buttons.Children.Count - 1).Where(tool => tool.IsVisible).ToList())
				{
					tool.IsVisible = false;
					hiddenForTitle.Add(tool);
				}
			}

			void ShowTools()
			{
				foreach (var tool in hiddenForTitle)
				{
					tool.IsVisible = true;
				}

				hiddenForTitle.Clear();
			}

			setlistTitle.PointerEntered += (_, e) =>
			{
				if (e.Pointer.Type != Avalonia.Input.PointerType.Touch)
				{
					ShowWholeTitle();
				}
			};
			setlistTitle.PointerExited += (_, e) =>
			{
				if (e.Pointer.Type != Avalonia.Input.PointerType.Touch)
				{
					ShowTools();
				}
			};
			setlistTitle.PointerReleased += (_, e) =>
			{
				if (e.Pointer.Type == Avalonia.Input.PointerType.Touch)
				{
					if (hiddenForTitle.Count > 0)
					{
						ShowTools();
					}
					else
					{
						ShowWholeTitle();
					}
				}
			};
			// The tools are back when the drawer opens again.
			setlistDrawer.PaneClosed += (_, _) => ShowTools();

			Grid.SetColumn(buttons, 1);
			Grid.SetColumn(endSearchButton, 1);
			var titleRow = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,Auto"),
				Children = { setlistTitle, setlistSearchBox, buttons, endSearchButton },
			};
			titleRow.Margin = new Thickness(8, 0, 0, 4);
			DockPanel.SetDock(titleRow, Dock.Top);

			// As wide as the open drawer whatever room it has, so it slides in and out as it is instead
			// of being squeezed while the drawer opens or closes.
			return new DockPanel
			{
				Margin = new Thickness(4, 4, 4, 0),
				Width = setlistDrawer.OpenPaneLength - 8,
				HorizontalAlignment = HorizontalAlignment.Left,
				Children = { titleRow, TopDocked(drawerTagFilter), setlistDrawerBody },
			};
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
				setlistTitle.Text = L.T("Setlist");
				setlistDrawerBody.Content = DrawerMessage(L.T("No setlist is selected yet. Choose one from the list of setlists."));
				return;
			}

			var sameSetlist = drawerSetlistId == setlist.Id;
			if (!sameSetlist)
			{
				setlistTitle.Text = setlist.Name;
				setlistDrawerBody.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8) };
			}

			try
			{
				var entries = await EntriesAsync(setlist);
				if (load != drawerLoad)
				{
					return;
				}

				setlistTitle.Text = setlist.Name;
				drawerSetlistId = setlist.Id;
				drawerRows.Clear();
				if (entries.Count == 0)
				{
					setlistDrawerBody.Content = DrawerMessage(L.T("There are no songs in this setlist yet."));
					return;
				}

				var list = new StackPanel { Spacing = 4 };
				foreach (var entry in entries)
				{
					var row = RoundedHighlight(SongRow(group, setlist, entry));
					row.Tag = entry.Song; // For the search (name and tags).
					drawerRows[entry.Id] = row;
					list.Children.Add(row);
				}

				ShowDrawerTags(entries.Select(entry => entry.Song));
				FilterDrawerSongs();

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
					setlistDrawerBody.Content = DrawerMessage(L.T("Couldn't load the songs. {0}", ex.Message));
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

		/// <summary>Lists only the songs with the search text in their name (every song when there's none).</summary>
		private void FilterDrawerSongs()
		{
			var text = setlistSearchBox.Text?.Trim() ?? string.Empty;
			foreach (var row in drawerRows.Values)
			{
				row.IsVisible = row.Tag is not SongDetails song
					|| ((text.Length == 0 || DrawerSongMatches(song, text)) && drawerChosenTags.All(tag => SongTags.Has(song, tag)) && !drawerWithoutTags.Any(tag => SongTags.Has(song, tag)));
			}
		}

		/// <summary>
		/// The tags of the setlist's songs as small buttons under the drawer's search box (shown
		/// while searching), to narrow the list to the songs with (or without) them; see ShowDrawerTagButtons.
		/// Tags chosen before that the songs still have stay chosen.
		/// </summary>
		private void ShowDrawerTags(IEnumerable<SongDetails> songs)
		{
			drawerTags = SongTags.AllOf(songs);
			drawerChosenTags.IntersectWith(drawerTags);
			drawerWithoutTags.IntersectWith(drawerTags);
			ShowDrawerTagButtons();
		}

		// The tags of the right drawer's songs, for its tag buttons.
		private IReadOnlyList<string> drawerTags = [];

		/// <summary>
		/// The drawer's tag buttons (see <see cref="TagFilterButton"/>): clicked, a tag lists only the
		/// songs with it; longclicked, only those without it.
		/// </summary>
		private void ShowDrawerTagButtons()
		{
			drawerTagFilter.Children.Clear();
			foreach (var tag in drawerTags)
			{
				var state = drawerWithoutTags.Contains(tag) ? TagFilterState.Without : drawerChosenTags.Contains(tag) ? TagFilterState.With : TagFilterState.Off;
				drawerTagFilter.Children.Add(TagFilterButton.Create(tag, state, 12, new Thickness(8, 1), next =>
				{
					drawerChosenTags.Remove(tag);
					drawerWithoutTags.Remove(tag);
					if (next == TagFilterState.With)
					{
						drawerChosenTags.Add(tag);
					}
					else if (next == TagFilterState.Without)
					{
						drawerWithoutTags.Add(tag);
					}

					FilterDrawerSongs();
				}));
			}

			drawerTagFilter.IsVisible = setlistSearchBox.IsVisible && drawerTags.Count > 0;
		}

		private static Control TopDocked(Control control)
		{
			DockPanel.SetDock(control, Dock.Top);
			return control;
		}

		/// <summary>
		/// Whether the right drawer's search finds the song: the text in its name, or in one of its
		/// tags ("#" in front of the text is left out, as tags are written without one).
		/// </summary>
		private static bool DrawerSongMatches(SongDetails song, string text)
		{
			if (song.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
			{
				return true;
			}

			var tag = text.TrimStart('#').Trim();
			return tag.Length > 0 && SongTags.Parse(song.Tags).Any(t => t.Contains(tag, StringComparison.CurrentCultureIgnoreCase));
		}

		/// <summary>The first song listed in the right drawer (the first found, while searching).</summary>
		private Button? FirstDrawerSongShown()
			=> setlistDrawerBody.Content is ScrollViewer { Content: Panel list } ? list.Children.OfType<Button>().FirstOrDefault(row => row.IsVisible) : null;

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
				// The current song may have changed: bring it to the middle of an open drawer (not when
				// it was clicked in the drawer: it's in sight there already).
				if (setlistDrawer.IsPaneOpen && !chosenInDrawer)
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

		/// <summary>One song in the right drawer: its name, then key, tempo, beat and tags; the current song is highlighted.</summary>
		private Button SongRow(GroupSummary group, SetlistSummary setlist, SetlistEntry entry)
		{
			var details = PageLayout.SongInfo(entry.Song);
			var content = new StackPanel { Spacing = 2 };
			content.Children.Add(new TextBlock { Text = entry.Song.Name, FontSize = 18, TextTrimming = TextTrimming.CharacterEllipsis });
			var tags = SongTags.Sorted(entry.Song.Tags);
			if (details.Length > 0 || tags.Count > 0)
			{
				// At the right: the tags, then key, tempo and beat. When the drawer is too narrow for
				// all of them, the tags wrap onto more lines.
				var detailsText = PageLayout.SongInfoText(entry.Song, fontSize: 13);
				DockPanel.SetDock(detailsText, Dock.Right);
				var chips = PageLayout.TagChips(tags, 11);
				chips.Margin = new Thickness(0, 0, details.Length > 0 && tags.Count > 0 ? 8 : 0, 0);
				chips.VerticalAlignment = VerticalAlignment.Center;
				chips.HorizontalAlignment = HorizontalAlignment.Right;
				content.Children.Add(new DockPanel { Children = { detailsText, chips } });
			}

			var row = new Button
			{
				Content = content,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Padding = new Thickness(10, 5),
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

				// The song is chosen at once; the drawer stays a moment, showing it, and then slides away.
				// The list stays where it is (the song was clicked where it is), not centred on it.
				CloseSetlistDrawerSlowly();
				currentEntryId = entry.Id;
				HighlightDrawerSong();
				chosenInDrawer = true;
				try
				{
					SelectSong(group, setlist, entry);
				}
				finally
				{
					chosenInDrawer = false;
				}
			};
			return row;
		}

		/// <summary>How long the right drawer stays after a song is chosen in it, and how long it then takes to slide away.</summary>
		private static readonly TimeSpan DrawerStay = TimeSpan.FromSeconds(1);
		private static readonly TimeSpan DrawerSlide = TimeSpan.FromSeconds(1);
		private readonly DispatcherTimer drawerStayTimer = new() { Interval = DrawerStay };
		private Panel? slowPaneRoot;

		/// <summary>
		/// Closes the right drawer (unless pinned) after a moment, sliding slowly. Another song chosen
		/// meanwhile starts the wait again; a drawer closed some other way meanwhile is simply closed.
		/// </summary>
		private void CloseSetlistDrawerSlowly()
		{
			drawerStayTimer.Stop();
			if (!IsPinned(setlistDrawer))
			{
				drawerStayTimer.Start();
			}
		}

		private void SlideSetlistDrawerAway()
		{
			drawerStayTimer.Stop();
			if (IsPinned(setlistDrawer) || !setlistDrawer.IsPaneOpen)
			{
				return;
			}

			// The drawer's own closing is quick (the theme's); this one time it takes longer.
			slowPaneRoot = setlistDrawer.GetVisualDescendants().OfType<Panel>().FirstOrDefault(panel => panel.Name == "PART_PaneRoot");
			if (slowPaneRoot != null)
			{
				slowPaneRoot.Transitions =
				[
					new Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = DrawerSlide, Easing = new Avalonia.Animation.Easings.CubicEaseInOut() },
				];
				// Back to the theme's speed once it's away (or when it's opened again before that).
				var slide = ++drawerSlide;
				DispatcherTimer.RunOnce(() =>
				{
					if (slide == drawerSlide)
					{
						EndSlowDrawerSlide();
					}
				}, DrawerSlide + TimeSpan.FromMilliseconds(100));
				// It's going away: clicks go through it to what's underneath, e.g. the play button
				// (at the far right, the last to come out), instead of being lost on the drawer.
				slowPaneRoot.IsHitTestVisible = false;
			}

			setlistDrawer.IsPaneOpen = false;
		}

		// Counts the slow slides (and slides back), so a timer of an earlier one leaves a later one alone.
		private int drawerSlide;

		// While the drawer slides back in (see SlideSetlistDrawerBack): its opening keeps that slide.
		private bool drawerSlidingBack;

		// While a song clicked in the right drawer is shown: the drawer isn't centred on it.
		private bool chosenInDrawer;

		// The drawer slid back in after a press during its slide away: once open, it isn't centred.
		private bool keepDrawerScroll;

		/// <summary>
		/// A press on the drawer while it slides away: it stops, and comes back quickly (from where it
		/// got to). The press is used up, so it doesn't reach the page underneath.
		/// </summary>
		private void SlideSetlistDrawerBack()
		{
			if (slowPaneRoot is not { } pane)
			{
				return;
			}

			var slide = ++drawerSlide;
			pane.Transitions =
			[
				new Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
			];
			pane.ClearValue(IsHitTestVisibleProperty);
			keepDrawerScroll = true;
			drawerSlidingBack = true;
			try
			{
				setlistDrawer.IsPaneOpen = true;
			}
			finally
			{
				drawerSlidingBack = false;
			}

			// Back to the theme's speed once it's in.
			DispatcherTimer.RunOnce(() =>
			{
				if (slide == drawerSlide)
				{
					EndSlowDrawerSlide();
				}
			}, TimeSpan.FromMilliseconds(350));
		}

		/// <summary>Whether the press is on what's still showing of the drawer sliding away.</summary>
		private bool PressOnSlidingDrawer(Avalonia.Input.PointerPressedEventArgs e)
		{
			if (slowPaneRoot is not { } pane || setlistDrawer.IsPaneOpen || pane.Bounds.Width < 1)
			{
				return false;
			}

			var point = e.GetPosition(pane);
			return point.X >= 0 && point.Y >= 0 && point.X <= pane.Bounds.Width && point.Y <= pane.Bounds.Height;
		}

		private void EndSlowDrawerSlide()
		{
			slowPaneRoot?.ClearValue(TransitionsProperty);
			slowPaneRoot?.ClearValue(IsHitTestVisibleProperty);
			slowPaneRoot = null;
		}

		/// <summary>
		/// A press outside the setlist drawer while it lingers after a song was picked sends it away
		/// at once. Otherwise the drawer would close only when the press is let go, and swallow it
		/// doing so (as a drawer that slides over the page does), so a button pressed then (play,
		/// say) would need pressing again.
		/// </summary>
		private void OnPressWhileDrawerLingers(object? sender, Avalonia.Input.PointerPressedEventArgs e)
		{
			// Sliding away already: a press on the drawer brings it back (one beside it goes through).
			if (PressOnSlidingDrawer(e))
			{
				e.Handled = true;
				SlideSetlistDrawerBack();
				return;
			}

			if (!drawerStayTimer.IsEnabled || setlistDrawer.Pane is not Visual pane
			    || (e.Source is Visual pressed && (pressed == pane || pane.IsVisualAncestorOf(pressed))))
			{
				return;
			}

			SlideSetlistDrawerAway();
		}

		/// <summary>How far one step of the mouse wheel scrolls, as a scroll view does it.</summary>
		private const double WheelStep = 50;

		/// <summary>
		/// An open drawer that slides over the page (not pinned) covers the page with a layer that
		/// closes it when tapped, which also takes the mouse wheel. So scrolling beside such a drawer
		/// scrolls the page's scroll view under the pointer instead (the lyrics, say), and the drawer
		/// stays open. Ctrl/⌘ + wheel (zoom) is left alone.
		/// </summary>
		private void OnWheelBesideOpenDrawer(object? sender, Avalonia.Input.PointerWheelEventArgs e)
		{
			var overlaid = (menuDrawer.IsPaneOpen && !IsPinned(menuDrawer)) || (setlistDrawer.IsPaneOpen && !IsPinned(setlistDrawer));
			if (!overlaid || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)
			    || e.Source is not Visual source || InPane(menuDrawer) || InPane(setlistDrawer))
			{
				return;
			}

			// The innermost scroll view on the page under the pointer that can scroll.
			ScrollViewer? target = null;
			foreach (var viewer in page.GetVisualDescendants().OfType<ScrollViewer>())
			{
				if (viewer.IsEffectivelyVisible && viewer.Extent.Height > viewer.Viewport.Height
				    && e.GetPosition(viewer) is var point && point.X >= 0 && point.Y >= 0 && point.X < viewer.Bounds.Width && point.Y < viewer.Bounds.Height)
				{
					target = viewer;
				}
			}

			if (target == null)
			{
				return;
			}

			var maxOffset = Math.Max(0, target.Extent.Height - target.Viewport.Height);
			target.Offset = new Vector(target.Offset.X, Math.Clamp(target.Offset.Y - e.Delta.Y * WheelStep, 0, maxOffset));
			e.Handled = true;

			bool InPane(SplitView drawer) => drawer.IsPaneOpen && drawer.Pane is Visual pane && (pane == source || pane.IsVisualAncestorOf(source));
		}

		private static TextBlock DrawerMessage(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

		private static Button DrawerItem(string text)
		{
			return RoundedHighlight(new Button
			{
				Content = text,
				FontSize = 17,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(10, 7),
			});
		}

		/// <summary>
		/// Rounds a drawer button's highlights (the left drawer's items, the right one's songs): its background (seen when pointing at or pressing
		/// it) and the outline it gets with the keyboard focus (e.g. moving with the arrow keys),
		/// which is square otherwise.
		/// </summary>
		private static Button RoundedHighlight(Button button)
		{
			var radius = new CornerRadius(8);
			button.CornerRadius = radius;
			button.FocusAdorner = new Avalonia.Controls.Templates.FuncTemplate<Control>(() =>
			{
				var outline = new Border { BorderThickness = new Thickness(2), CornerRadius = radius };
				outline.Bind(Border.BorderBrushProperty, outline.GetResourceObservable(LyriousTheme.SelectionBrush));
				return outline;
			});
			return button;
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

		/// <summary>After login: the start page, with the screen of what's new since the last visit over it.</summary>
		private Task StartAsync()
		{
			// The singer while the bands are fetched (the first sync after login), until the start page.
			page.Content = new SplashView(L.T("Getting your songs and setlists…"));
			return RunAsync(async () =>
			{
				await OpenStartPageAsync();
				if (!skipOnboarding)
				{
					await ShowOnboardingAsync();
				}
			});
		}

		// Made anew (e.g. in another language): no screen of what's new.
		private readonly bool skipOnboarding;

		/// <summary>Whether the lyrics are playing now (so the screens aren't made anew under them).</summary>
		public bool IsPlayingLyrics => IsPlaying;

		/// <summary>
		/// Shows what changed since the member's last visit on this device, and the release notes since
		/// then; the first time, an introduction to the app. Not shown when nothing is new. Continue (or ✕)
		/// goes back to the start page.
		/// </summary>
		private async Task ShowOnboardingAsync()
		{
			var key = SettingKeys.LastVisit(user.UserName);
			DateTime? lastVisit = DateTime.TryParse(App.Settings.Get(key), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var saved) ? saved : null;
			IReadOnlyList<Change>? changes = null;
			if (lastVisit is { } since)
			{
				try
				{
					changes = await service.GetChangesSinceAsync(since);
				}
				catch (Exception ex)
				{
					// Shown anyway, with the release notes; it says the changes couldn't be found out.
					System.Diagnostics.Debug.WriteLine($"Couldn't get the changes since the last visit: {ex.Message}");
				}
			}

			// Shown the first time, and later only when something is new: changes, or release notes.
			var anythingNew = lastVisit == null || changes is { Count: > 0 } || ReleaseNotes.Since(lastVisit).Count > 0;
			if (anythingNew)
			{
				var view = new OnboardingView(user.DisplayName, lastVisit, changes);
				view.Continued += GoBack;
				page.Content = view;
			}

			// Seen: next time, what changed from now on (changes made here meanwhile are left out).
			App.Settings.Set(key, DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
			service.ForgetLocalChanges();
		}

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
			groupName.Text = start.Group?.Name ?? L.T("Choose a band");
			// The private band's name in italics (it's not a band as such); a band's in bold.
			var isPrivate = start.Group?.IsPrivate == true;
			groupName.FontWeight = isPrivate ? FontWeight.Normal : FontWeight.SemiBold;
			groupName.FontStyle = isPrivate ? FontStyle.Italic : FontStyle.Normal;
			currentGroup = start.Group;
			_ = ShowPresentMembersAsync();
			currentSetlist = null;
			SetCurrentSong(null, null);
			ShowSetlistName(null);
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
					// No song yet: the band's setlist is the current one (in the right drawer), and
					// the page says a song is still to be chosen.
					SetCurrentSetlist(start.Setlist!);
					ShowNoSong();
					break;
				case StartPageKind.Setlists:
					ShowNoSong();
					break;
				default:
					// Offline with no copy on this device yet, nothing can be shown: say why.
					ShowMessage(service.Status is { Online: false, LastSynced: null }
						? L.T("You're offline, and this device has no copy of your bands yet. Connect to the server once to sync, then the app works offline too.")
						: L.T("You're not a member of a band yet. Ask a band admin to invite you."));
					break;
			}

			RefreshSetlistDrawer(reload: true);
		}

		/// <summary>The member's bands with their members, to choose the one to work with, and manage it.</summary>
		/// <param name="selectedGroupId">The band to show selected (e.g. the one just managed); else the joined one.</param>
		private async Task ShowGroupsAsync() => await ShowGroupsAsync(null);

		/// <param name="replace">Updates the band page in place (✕ doesn't go back to the old one).</param>
		private async Task ShowGroupsAsync(Guid? selectedGroupId, bool replace = false)
		{
			var groups = await service.GetGroupsAsync();
			var members = await service.GetGroupMembersAsync();
			var view = new GroupsView(groups, members, currentGroup?.Id, selectedGroupId);
			// Choosing a band changes the current one but stays in the band list, now with that band highlighted.
			view.GroupSelected += group => _ = RunAsync(async () =>
			{
				await service.SelectGroupAsync(group.Id);
				await OpenStartPageAsync(showPage: false);
				await ShowGroupsAsync(group.Id, replace: true);
			});
			view.MemberRoleChangeRequested += (group, member, role) => _ = RunAsync(async () =>
			{
				await service.SetMemberRoleAsync(group.Id, member.MemberId, role);
				await ShowGroupsAsync(group.Id, replace: true);
			});
			view.MemberRenameRequested += (group, member, name) => _ = RunAsync(async () =>
			{
				await service.RenameMemberAsync(member.MemberId, name);
				await ShowGroupsAsync(group.Id, replace: true);
				_ = ShowPresentMembersAsync();
			});
			// Renamed: the page (and, for the joined band, the menu) show the new name.
			view.MidiExpressionsChangeRequested += (group, expressions) => _ = RunAsync(async () =>
			{
				await service.SetMidiExpressionsAsync(group.Id, expressions);
				await ShowGroupsAsync(group.Id, replace: true);
			});
			view.GroupRenameRequested += (group, name) => _ = RunAsync(async () =>
			{
				await service.RenameGroupAsync(group.Id, name);
				if (group.Id == currentGroup?.Id)
				{
					currentGroup = currentGroup with { Name = name };
					groupName.Text = name;
				}

				await ShowGroupsAsync(group.Id, replace: true);
			});
			view.MemberRemoveRequested += (group, member) => _ = RunAsync(async () =>
			{
				await service.RemoveMemberAsync(group.Id, member.MemberId);
				await ShowGroupsAsync(group.Id, replace: true);
			});
			view.GroupDeleteRequested += group => _ = RunAsync(async () =>
			{
				await service.DeleteGroupAsync(group.Id);
				if (group.Id == currentGroup?.Id)
				{
					// The joined band is gone: the start page picks another (or offers to add one).
					await OpenStartPageAsync(showPage: false);
				}

				await ShowGroupsAsync(null, replace: true);
			});
			view.InviteRequested = async (group, query) =>
			{
				try
				{
					if (await service.FindMemberAsync(query) is not { } found)
					{
						// Someone without an account can't be in the band yet, but can be asked by
						// email to create one.
						if (!query.Contains('@'))
						{
							return L.T("No member has the username, email or phone \"{0}\". To invite someone who has no account yet, enter their email address.", query);
						}

						await service.InviteByEmailAsync(group.Id, query);
						ShowNotice(L.T("{0} has no account yet: sent them an email invitation", query));
						await ShowGroupsAsync(group.Id, replace: true);
						return null;
					}

					await service.InviteMemberAsync(group.Id, found.Id);
					ShowNotice(L.T("Invited {0} to {1}", found.Name, group.Name));
					await ShowGroupsAsync(group.Id, replace: true);
					return null;
				}
				catch (Exception ex)
				{
					return ex.Message;
				}
			};
			// A new band becomes the current one straight away.
			view.GroupCreateRequested += name => _ = RunAsync(async () =>
			{
				var group = await service.CreateGroupAsync(name);
				await service.SelectGroupAsync(group.Id);
				await OpenStartPageAsync();
			});
			ShowPage(view, replace);
		}

		/// <param name="replace">The page shown now isn't kept to go back to (e.g. going back to this one).</param>
		/// <param name="selectedSetlistId">The setlist to show; else the current one.</param>
		private void ShowSetlists(GroupSummary group, IReadOnlyList<SetlistSummary> setlists, bool replace = false, Guid? selectedSetlistId = null)
		{
			var view = new SetlistsView(group, setlists, currentSetlist?.Id, setlist => service.GetEntriesAsync(setlist.Id), selectedSetlistId);
			// A song picked in a setlist opens it (and makes that setlist the current one).
			view.SongSelected += (setlist, entry) => SelectSong(group, setlist, entry);
			// "+" on the shown setlist: the same menu as the right drawer's, for that setlist.
			view.AddSongsRequested += (setlist, button) => AddSongsMenu(() => (group, setlist)).ShowAt(button);
			// Made the current one: the page shows it so, and the drawer and the band follow.
			view.SetlistMadeCurrent += setlist => _ = RunAsync(async () =>
			{
				if (bandSync)
				{
					await service.SelectSetlistAsync(group.Id, setlist.Id);
				}

				SetCurrentSong(null, null);
				SetCurrentSetlist(setlist);
				playbackEntries = [];
				RefreshSetlistDrawer(reload: true);
				_ = UpdatePlaybackNavigationAsync();
				ShowSetlists(group, setlists, replace: true);
			});
			// Renamed: the page (and, for the current setlist, the drawers) show the new name.
			view.SetlistRenameRequested += (setlist, name) => _ = RunAsync(async () =>
			{
				await service.RenameSetlistAsync(setlist.Id, name);
				if (setlist.Id == currentSetlist?.Id)
				{
					SetCurrentSetlist(setlist with { Name = name });
					RefreshSetlistDrawer(reload: false);
				}

				ShowSetlists(group, await service.GetSetlistsAsync(group.Id), replace: true, selectedSetlistId: setlist.Id);
			});
			view.SetlistDeleteRequested += setlist => _ = RunAsync(async () =>
			{
				await service.DeleteSetlistAsync(setlist.Id);
				if (setlist.Id == currentSetlist?.Id)
				{
					// Nothing is current any more; the start page picks another setlist next time.
					currentSetlist = null;
					ShowSetlistName(null);
					addSongsButton.IsEnabled = false;
					SetCurrentSong(null, null);
					playbackEntries = [];
					RefreshSetlistDrawer(reload: true);
				}

				ShowSetlists(group, await service.GetSetlistsAsync(group.Id), replace: true);
			});
			// A new setlist is shown straight away, to add songs to.
			view.SetlistCreateRequested += name => _ = RunAsync(async () =>
			{
				var created = await service.CreateSetlistAsync(group.Id, name);
				ShowSetlists(group, await service.GetSetlistsAsync(group.Id), replace: true, selectedSetlistId: created.Id);
			});
			ShowPage(view, replace);
		}

		/// <summary>
		/// The songs of a setlist. It's just shown: it becomes the current one only with
		/// <paramref name="makeCurrent"/> (the band's setlist, the start page's), or when a song in it
		/// is picked. The page stays on this setlist when the current one changes meanwhile.
		/// </summary>
		private async Task ShowSetlistAsync(GroupSummary group, SetlistSummary setlist, bool makeCurrent = false)
		{
			var entries = await EntriesAsync(setlist);
			if (makeCurrent)
			{
				SetCurrentSetlist(setlist);
			}

			// The current setlist's songs may have changed (songs added): the drawer and next song follow.
			var isCurrent = setlist.Id == currentSetlist?.Id;
			if (isCurrent)
			{
				playbackEntries = entries;
			}

			var view = new SetlistView(setlist, entries);
			view.SongSelected += entry => SelectSong(group, setlist, entry);
			view.AddSongsRequested += () => _ = RunAsync(async () =>
				ShowAddSongs(group, setlist, await service.GetSongbookSongsAsync(group.Id), entries.Select(entry => entry.Song.Id).ToHashSet()));
			page.Content = view;
			if (isCurrent)
			{
				RefreshSetlistDrawer(reload: true);
			}
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
			// A song from a setlist that wasn't the current one (one only being looked at) makes it current.
			var setlistChanged = setlist.Id != currentSetlist?.Id;
			SetCurrentSetlist(setlist);
			SetCurrentSong(entry.Song, entry.Id);
			if (setlistChanged)
			{
				playbackEntries = [];
			}

			var view = new LyricScrollView(entry.Song, edited => service.UpdateSongAsync(edited));
			UseMySetting(view, group.Id, entry.Song.Id);
			ShowMidiProblems(view, group.Id);
			ShowLyricsPage(view, group.Id);
			// The song's MIDI tags set the instruments, effects and lights for it.
			SendSongSelectMidi(group.Id, entry.Song.Lyrics);
			RefreshSetlistDrawer(reload: setlistChanged);
		}

		/// <summary>
		/// The songbooks to choose between on the songbook page: the private band's and the joined
		/// band's. None (nothing to choose) when the joined band is the private one.
		/// </summary>
		private async Task<IReadOnlyList<GroupSummary>> SongbookChoicesAsync()
		{
			if (currentGroup is not { IsPrivate: false } joined)
			{
				return [];
			}

			var own = (await service.GetGroupsAsync()).FirstOrDefault(group => group.IsPrivate);
			return own == null ? [] : [own, joined];
		}

		/// <summary>
		/// A band's songbook (the joined band's, or the private one), to choose a song. A song that is
		/// in the current setlist opens from there (and becomes the group's current song); any other
		/// song just shows its lyrics.
		/// </summary>
		/// <param name="replace">Takes the current songbook page's place (after a change to it), so ✕ doesn't go back to the old one.</param>
		private async Task ShowSongsAsync(GroupSummary group, bool replace = false)
		{
			var songs = await service.GetSongbookSongsAsync(group.Id);
			// The songbook played as the setlist (its id is its band's), for the note on its button.
			var playing = currentSetlist is { } shownSetlist && IsSongbookSetlist(shownSetlist) ? shownSetlist.GroupId : (Guid?)null;
			var view = new SongsView(group, songs, currentSongId, await SongbookChoicesAsync(), playing);
			// Another songbook chosen: shown in this page's place.
			view.SongbookChosen += chosen => _ = RunAsync(() => ShowSongsAsync(chosen, replace: true));
			// A new setlist of the band's with the songs, in order; the songbook stays on the page.
			view.SaveAsSetlistRequested += (name, chosen) => _ = RunAsync(async () =>
			{
				var setlist = await service.CreateSetlistAsync(group.Id, name);
				await service.AddSongsAsync(setlist.Id, chosen.Select(song => song.Id).ToList());
				ShowNotice(L.Plural(chosen.Count, "Saved the setlist {1} ({0} song)", "Saved the setlist {1} ({0} songs)", name));
			});
			view.ShowAsSetlistRequested += shown =>
			{
				ShowSongbookAsSetlist(group, shown);
				// Again in its place, now with the note on this songbook's button (the search and tags stay).
				_ = RunAsync(() => ShowSongsAsync(group, replace: true));
			};
			view.SongSelected += song => _ = RunAsync(async () =>
			{
				if (currentSetlist is { } setlist
				    && (await EntriesAsync(setlist)).FirstOrDefault(entry => entry.Song.Id == song.Id) is { } entry)
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
			view.LoadSetlists = () => service.GetSetlistsAsync(group.Id);
			view.SongAddToSetlistRequested += (song, setlist) => _ = AddSongToSetlistAsync(song, setlist);
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
				await ShowSongsAsync(group, replace: true);
			});
			// Several songs at once (the checked ones, from the setlist tool), as one above.
			view.SongsDeleteRequested += songs => _ = RunAsync(async () =>
			{
				foreach (var song in songs)
				{
					await service.DeleteSongAsync(song.Id);
				}

				if (songs.Any(song => song.Id == currentSongId))
				{
					SetCurrentSong(null, null);
				}

				// The songbook played as the setlist holds its own copy of the songs.
				var deleted = songs.Select(song => song.Id).ToHashSet();
				songbookEntries = songbookEntries.Where(entry => !deleted.Contains(entry.Song.Id)).ToArray();
				playbackEntries = [];
				RefreshSetlistDrawer(reload: true);
				ShowNotice(L.Plural(songs.Count, "Deleted {0} song", "Deleted {0} songs"));
				await ShowSongsAsync(group, replace: true);
			});
			ShowPage(view, replace);
		}

		/// <summary>
		/// Adds a song from the songbook to the end of a setlist. The songbook stays up; a notice says
		/// how it went.
		/// </summary>
		private async Task AddSongToSetlistAsync(SongDetails song, SetlistSummary setlist)
		{
			try
			{
				await service.AddSongsAsync(setlist.Id, [song.Id]);
			}
			catch (Exception ex)
			{
				ShowNotice(L.T("Couldn't add \"{0}\" to {1}. {2}", song.Name, setlist.Name, ex.Message));
				return;
			}

			if (setlist.Id == currentSetlist?.Id)
			{
				// The current setlist is longer now: the drawer and the next/previous song follow.
				playbackEntries = [];
				RefreshSetlistDrawer(reload: true);
			}

			ShowNotice(L.T("Added \"{0}\" to {1}.", song.Name, setlist.Name));
		}

		/// <summary>A song was edited: its name, key, tempo and beat show in the menu and the setlists too.</summary>
		private void SongEdited(SongDetails saved)
		{
			// The songbook shown as a setlist holds its own copy of the song.
			songbookEntries = songbookEntries.Select(entry => entry.Song.Id == saved.Id ? entry with { Song = saved } : entry).ToArray();
			if (playbackEntries.Count > 0 && currentSetlist is { } shown && IsSongbookSetlist(shown))
			{
				playbackEntries = songbookEntries;
			}

			if (saved.Id == currentSongId)
			{
				SetCurrentSong(saved, currentEntryId);
			}

			playbackEntries = [];
			RefreshSetlistDrawer(reload: true);
		}

		/// <summary>Edits a song's name, key, tempo, beat and lyrics; back to the songbook afterwards.</summary>
		private void ShowSongEditor(GroupSummary group, SongDetails song)
		{
			// The same editor as the pencil on a song's lyrics.
			var view = new LyricScrollView(song, edited => service.UpdateSongAsync(edited), startEditing: true) { EditorOnly = true };
			UseMySetting(view, group.Id, song.Id);
			ShowMidiProblems(view, group.Id);
			// Back to the songbook it was opened from (nothing changed), which ✕ then leaves as usual.
			view.EditCancelled += GoBack;
			view.SongSaved += saved => _ = RunAsync(async () =>
			{
				SongEdited(saved);
				// The songbook again, updated, in the old one's place: neither the editor nor the old
				// songbook is left to go back to.
				ForgetBackTo<SongsView>();
				await ShowSongsAsync(group, replace: true);
			});
			page.Content = view;
		}

		private void OpenInternetSearch()
		{
			if (currentGroup is { } group)
			{
				OpenFromDrawer();
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
			var addText = addToSetlist == null ? L.T("Add to songbook") : L.T("Add to {0}", addToSetlist.Name);
			var view = new InternetSongView(query => service.SearchInternetAsync(query), names, addText);
			view.AddRequested += found => _ = RunAsync(async () =>
			{
				var song = await service.CreateSongAsync(group.Id, found.Title, found.Lyrics, found.Duration);
				if (!string.IsNullOrWhiteSpace(found.Artist))
				{
					song = await service.UpdateSongAsync(song with { Artist = found.Artist.Trim() });
				}

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
		/// <param name="startEditing">Opens the editor straight away (for a new empty song).</param>
		private void ShowSongbookLyrics(GroupSummary group, SongDetails song, bool startEditing = false)
		{
			SetCurrentSong(song, null);

			var view = new LyricScrollView(song, edited => service.UpdateSongAsync(edited), startEditing);
			UseMySetting(view, group.Id, song.Id);
			ShowMidiProblems(view, group.Id);
			ShowLyricsPage(view, group.Id);
			if (!startEditing)
			{
				SendSongSelectMidi(group.Id, song.Lyrics);
			}
			RefreshSetlistDrawer();
		}

		/// <summary>Gives the lyrics view the band's MIDI expressions, so its editor points out what in the MIDI tags can't be sent.</summary>
		private async void ShowMidiProblems(LyricScrollView view, Guid groupId)
		{
			// The tags used in the band's songbook, offered while the song's tags are typed.
			view.LoadUsedTags = async () => SongTags.AllOf(await service.GetSongbookSongsAsync(groupId));
			try
			{
				var band = (await service.GetGroupsAsync()).FirstOrDefault(g => g.Id == groupId);
				view.MidiExpressions = Midi.ExpressionsFromJson(band?.MidiExpressions);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't read the band's MIDI expressions: {ex.Message}");
			}
		}

		// MIDI goes out in the order it's asked for (e.g. a song's stop before the next song's select),
		// though the band's MIDI settings are read before each send.
		private Task midiSends = Task.CompletedTask;

		/// <summary>
		/// A song is selected: sends the band's commands for that, then the MIDI tags at the start of
		/// the song's lyrics.
		/// </summary>
		private void SendSongSelectMidi(Guid groupId, string lyrics)
			=> SendMidi(groupId, (expressions, commands) => Midi.Parse(commands.Select, expressions).Concat(Midi.SongStart(lyrics, expressions)).ToList());

		/// <summary>
		/// Queues MIDI to send: <paramref name="build"/> makes it from the band's MIDI expressions and
		/// commands as they are now (read from the band, so a change shows at once).
		/// </summary>
		private void SendMidi(Guid groupId, Func<IReadOnlyList<MidiExpression>, MidiSongCommands, IReadOnlyList<byte[]>> build)
		{
			if (App.Midi != null)
			{
				midiSends = SendMidiAfterAsync(midiSends, groupId, build);
			}
		}

		private async Task SendMidiAfterAsync(Task before, Guid groupId, Func<IReadOnlyList<MidiExpression>, MidiSongCommands, IReadOnlyList<byte[]>> build)
		{
			await before;
			IReadOnlyList<MidiExpression> expressions = [];
			var commands = MidiSongCommands.None;
			try
			{
				var band = (await service.GetGroupsAsync()).FirstOrDefault(g => g.Id == groupId);
				expressions = Midi.ExpressionsFromJson(band?.MidiExpressions);
				commands = Midi.SongCommandsFromJson(band?.MidiCommands);
			}
			catch (Exception ex)
			{
				// Without the band's settings, a song's plain commands are still sent.
				System.Diagnostics.Debug.WriteLine($"Couldn't read the band's MIDI settings: {ex.Message}");
			}

			try
			{
				if (build(expressions, commands) is { Count: > 0 } messages)
				{
					App.Midi?.Send(messages);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Couldn't send MIDI: {ex.Message}");
			}
		}

		/// <param name="groupId">The band the song is shown from (its MIDI commands are sent when it starts and stops).</param>
		private void ShowLyricsPage(LyricScrollView view, Guid groupId)
		{
			// A tap on the lyrics shows the toolbars when they're hidden; it hides them only while the
			// lyrics scroll by themselves (when stopped, they stay, as a tap is then likely a mistake).
			view.FocusModeToggled += () =>
			{
				if (focusMode)
				{
					SetFocusMode(false);
				}
				else if (view.IsAutoScrolling)
				{
					SetFocusMode(true);
				}
			};
			view.SetFocusMode(focusMode);
			// The song's media file playing or stopping starts or stops the scrolling too, as play/pause
			// does (band sync, the playlog and the band's MIDI included).
			// The media player on or off (or another sound picked): the toolbar shows its progress or not.
			view.MediaChanged += () =>
			{
				if (page.Content == view)
				{
					playbackBar.SetMediaOn(view.ActiveSound != null);
				}
			};
			playbackBar.SetMediaOn(view.ActiveSound != null);
			view.MediaPlayingChanged += playing =>
			{
				if (page.Content == view && !view.IsEditing && playing != view.IsAutoScrolling)
				{
					PlayPause(goOnAtEnd: false);
				}
			};
			view.SongSaved += saved =>
			{
				SongEdited(saved);
				// The next song's name in the toolbar may be the one just renamed.
				_ = UpdatePlaybackNavigationAsync();
			};
			// Playing turns focus mode on, stopping (pause, or editing) turns it off.
			// A page that's been replaced (e.g. by the next song) no longer counts.
			view.AutoScrollChanged += () =>
			{
				// The band's MIDI commands for starting and stopping a song; also when it stops by being left.
				var started = view.IsAutoScrolling;
				SendMidi(groupId, (expressions, commands) => Midi.Parse(started ? commands.Start : commands.Stop, expressions));

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
					: await EntriesAsync(setlist);
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
		/// Focus mode on (also while playing): hides the drawers (keeping them pinned if they were)
		/// but not the buttons that open them, and leaves only the song's name, details and lyrics on
		/// the page, which also get the room of a pinned right drawer. Off: brings
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
				// The right drawer gives its room to the lyrics, also when pinned.
				setlistHiding = HideDrawer(setlistDrawer, keepRoom: false);
				// The buttons that open the drawers stay; also the one of a pinned drawer made
				// invisible, which shows that drawer again.
				if (menuHiding == DrawerHiding.Invisible)
				{
					topRow.Children[0].IsVisible = true;
				}

				Focus(); // So Escape reaches this view.
			}
			else
			{
				if (menuHiding == DrawerHiding.Invisible)
				{
					topRow.Children[0].IsVisible = false; // Its pinned drawer shows again.
				}

				ShowDrawer(menuDrawer, menuHiding);
				ShowDrawer(setlistDrawer, setlistHiding);
			}

			if (page.Content is LyricScrollView lyrics)
			{
				lyrics.SetFocusMode(on);
			}
		}

		/// <summary>
		/// Hides a drawer for focus mode. A drawer sliding over the page is closed. A pinned, open
		/// drawer stays open but is made invisible, so the page doesn't move (closing it would widen
		/// the page and make the lyrics move and rewrap); unless <paramref name="keepRoom"/> is false:
		/// then it's closed too (still pinned, for when it's shown again) and the page gets its room.
		/// </summary>
		private static DrawerHiding HideDrawer(SplitView drawer, bool keepRoom = true)
		{
			if (!drawer.IsPaneOpen)
			{
				return DrawerHiding.None;
			}

			if (!IsPinned(drawer) || !keepRoom)
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
		private void ShowSettings()
		{
			if (currentGroup is not { } group)
			{
				page.Content = new SettingsView();
				return;
			}

			// The band settings are made once the band (and the member's role in it) is read: an empty
			// place for them until then.
			var bandSettings = new ContentControl();
			page.Content = new SettingsView((anchor, convert) => ImportSongbookProAsync(anchor, group, convert), () => ClearSongbookAsync(group), group.Name, bandSettings);
			_ = ShowBandSettingsAsync(group.Id, bandSettings);
		}

		/// <summary>
		/// The band's settings (its MIDI for every song), saved with the band so they're the same for all
		/// its members; its admins change them, the others see them.
		/// </summary>
		private async Task ShowBandSettingsAsync(Guid groupId, ContentControl place)
		{
			try
			{
				var band = (await service.GetGroupsAsync()).FirstOrDefault(g => g.Id == groupId);
				var members = await service.GetGroupMembersAsync();
				var me = members.TryGetValue(groupId, out var list) ? list.FirstOrDefault(member => member.IsMe) : null;
				place.Content = new MidiSongCommandsSection(
					Midi.SongCommandsFromJson(band?.MidiCommands),
					Midi.ExpressionsFromJson(band?.MidiExpressions),
					canEdit: me?.Role == GroupRole.Admin,
					commands => service.SetMidiSongCommandsAsync(groupId, commands));
			}
			catch (Exception ex)
			{
				place.Content = new TextBlock { Text = L.T("Couldn't read the band settings. {0}", ex.Message), TextWrapping = TextWrapping.Wrap };
			}
		}

		/// <summary>Deletes every song in the band's songbook (they leave its setlists too); returns how many.</summary>
		private async Task<string> ClearSongbookAsync(GroupSummary group)
		{
			var songs = await service.GetSongbookSongsAsync(group.Id);
			foreach (var song in songs)
			{
				await service.DeleteSongAsync(song.Id);
			}

			// No song is current any more, and the setlists lost their songs.
			if (songs.Any(song => song.Id == currentSongId))
			{
				SetCurrentSong(null, null);
			}

			playbackEntries = [];
			RefreshSetlistDrawer(reload: true);
			return songs.Count == 0
				? L.T("{0}'s songbook was already empty.", group.Name)
				: L.Plural(songs.Count, "Deleted {0} song from {1}'s songbook.", "Deleted {0} songs from {1}'s songbook.", group.Name);
		}

		/// <summary>
		/// Picks a SongbookPro backup and imports its songs and sets into the band; returns what was
		/// imported and what was left out (null when no file was picked).
		/// </summary>
		private async Task<string?> ImportSongbookProAsync(Control anchor, GroupSummary group, bool convert)
		{
			if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { CanOpen: true } storage)
			{
				throw new InvalidOperationException(L.T("Opening files isn't possible here."));
			}

			var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
			{
				Title = L.T("Import a SongbookPro backup"),
				AllowMultiple = false,
				FileTypeFilter =
				[
					new Avalonia.Platform.Storage.FilePickerFileType(L.T("SongbookPro backup")) { Patterns = ["*.zip", "*.sbpbackup"] },
					Avalonia.Platform.Storage.FilePickerFileTypes.All,
				],
			});
			if (files.Count == 0)
			{
				return null;
			}

			// Read into memory first: a zip needs to move about in its file, which a browser's file can't.
			using var data = new MemoryStream();
			try
			{
				await using var stream = await files[0].OpenReadAsync();
				await stream.CopyToAsync(data);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				throw new InvalidOperationException(L.T("Couldn't read the file. {0}", ex.Message), ex);
			}

			data.Position = 0;
			var backup = SongbookProImport.Read(data, convert);
			var result = await SongbookProImport.ImportAsync(service, group.Id, backup);

			// The drawer's setlists and songs may include the new ones.
			RefreshSetlistDrawer(reload: true);

			var text = L.Plural(result.Songs, "Imported {0} song", "Imported {0} songs") + " " + L.Plural(result.Setlists, "and {0} setlist into {1}.", "and {0} setlists into {1}.", group.Name);
			if (result.Replaced > 0)
			{
				text += L.T(" {0} of the songs overwrote one of the same name.", result.Replaced);
			}

			if (result.ReplacedSetlists > 0)
			{
				text += L.T(" {0} of the setlists replaced the songs of one of the same name.", result.ReplacedSetlists);
			}

			if (result.Skipped.Count > 0)
			{
				text += $" Left out {result.Skipped.Count}: {string.Join(", ", result.Skipped)}.";
			}

			return text;
		}

		private void ShowProfile()
		{
			var view = new ProfileView(user with { DisplayName = memberName.Text ?? user.DisplayName }, service, App.AuthService, offerForgetSavedPassword);
			view.Renamed += name => memberName.Text = name;
			view.PasswordChanged += password => PasswordChanged?.Invoke(password);
			view.ForgetSavedPasswordRequested += () =>
			{
				offerForgetSavedPassword = false;
				ForgetSavedPasswordRequested?.Invoke();
			};
			view.LogOutRequested += () =>
			{
				menuDrawer.IsPaneOpen = false;
				StopLiveUpdates();
				LogOutRequested?.Invoke();
			};
			page.Content = view;
		}

		/// <summary>Shows a page; with <paramref name="replace"/>, the current one isn't kept to go back to.</summary>
		private void ShowPage(Control view, bool replace)
		{
			if (!replace)
			{
				page.Content = view;
				return;
			}

			goingBack = true;
			try
			{
				page.Content = view;
			}
			finally
			{
				goingBack = false;
			}
		}

		/// <summary>Forgets the pages to go back to after the last page of this kind, and that page too.</summary>
		private void ForgetBackTo<T>() where T : Control
		{
			var last = history.FindLastIndex(view => view is T);
			if (last >= 0)
			{
				history.RemoveRange(last, history.Count - last);
			}
		}

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

				// A list page as it was, with something else current then (another setlist, band or song
				// was chosen since): made anew, so what's current now is shown as current.
				if (previous is SetlistsView setlists && setlists.CurrentSetlistId != currentSetlist?.Id)
				{
					_ = RunAsync(async () => ShowSetlists(setlists.Group, await service.GetSetlistsAsync(setlists.Group.Id), replace: true));
					return;
				}

				if (previous is GroupsView bands && bands.CurrentGroupId != currentGroup?.Id)
				{
					_ = RunAsync(() => ShowGroupsAsync(bands.SelectedGroupId, replace: true));
					return;
				}

				if (previous is SongsView songbook && songbook.CurrentSongId != currentSongId)
				{
					_ = RunAsync(() => ShowSongsAsync(songbook.Group, replace: true));
					return;
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

			// The page left isn't one to go back to: it was the last one, and ✕ on the page shown now
			// would lead back to it, and ✕ there back here, round and round.
			_ = RunAsync(async () =>
			{
				await ReopenCurrentSongAsync();
				history.Clear();
			});
		}

		/// <summary>
		/// Opens the lyrics of the current song (from its setlist, or from the songbook); with no
		/// current song, the page that says none is chosen yet. Without a band, the start page.
		/// </summary>
		private async Task ReopenCurrentSongAsync()
		{
			if (currentGroup is not { } group)
			{
				await OpenStartPageAsync();
				return;
			}

			if (currentSetlist is { } setlist && currentEntryId is { } entryId && await EntryAsync(entryId) is { } entry)
			{
				ShowLyrics(group, setlist, entry);
				return;
			}

			if (currentSongId is { } songId && (await service.GetSongbookSongsAsync(group.Id)).FirstOrDefault(song => song.Id == songId) is { } song)
			{
				ShowSongbookLyrics(group, song);
				return;
			}

			ShowNoSong();
		}

		/// <summary>The page that says no song is chosen yet, with the ways to choose one.</summary>
		private void ShowNoSong()
		{
			if (currentGroup is not { } group)
			{
				return;
			}

			var view = new NoSongView(group.Name, currentSetlist?.Name);
			view.SongbookRequested += () => _ = RunAsync(() => ShowSongsAsync(group));
			view.SetlistsRequested += () => _ = RunAsync(async () => ShowSetlists(group, await service.GetSetlistsAsync(group.Id)));
			view.CurrentSetlistRequested += () =>
			{
				setlistDrawer.IsPaneOpen = true;
				_ = LoadSetlistDrawerAsync(centerSong: true);
			};
			page.Content = view;
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

			ShowNotice(L.T("Stop playing to change song."));
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

			// Tell the band first, so it goes out before the lyrics are laid out here.
			_ = SaveSongSelectionAsync(group, entry);
			ShowLyrics(group, setlist, entry);
		}

		private async Task SaveSongSelectionAsync(GroupSummary group, SetlistEntry entry)
		{
			// Band sync off, or a song of the songbook shown as a setlist (not saved, so the band can't have it).
			if (!bandSync || entry.SetlistId == group.Id)
			{
				return; // The band's current song stays as it is.
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
				// (The splash at start already says the app is busy.)
				if (await Task.WhenAny(task, Task.Delay(LoadingDelay)) != task && page.Content == previous && previous is not SplashView)
				{
					page.Content = new ProgressBar { IsIndeterminate = true, Width = 200, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
				}

				await task;
			}
			catch (Exception ex)
			{
				var retry = new Button { Content = L.T("Try again"), HorizontalAlignment = HorizontalAlignment.Center };
				retry.Click += (_, _) => _ = previous is null or SplashView ? StartAsync() : RunAsync(action);
				ShowMessage(L.T("Something went wrong. {0}", ex.Message), retry);
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
