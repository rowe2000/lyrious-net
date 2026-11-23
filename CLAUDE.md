# Lyrious

Setlists, songbooks and lyrics for bands: lyrics and chords that scroll in time with the song, shared
and followed live by the whole band.

Requirements:
* .NET10
* Avalonia 12
* Dark/Light Theme
* macOS, iOS, iPadOS, Windows, Android, Wasm

## Projects

- `Lyrious.UILib`: the whole UI (Avalonia, shared by every platform). Pages in `Setlists/`.
- `Lyrious.Desktop`, `Lyrious.Web` (WebAssembly), `Lyrious.Android`, `Lyrious.iOS`: thin platform
  hosts; they only plug in platform services (settings store, sound, MIDI, credentials).
- `Lyrious.Client`: the app's API client, offline cache and sync (`HttpSetlistService`,
  `HttpAuthService`, `Offline/`).
- `Lyrious.CoreLib`: models, `LyriousContext` (EF Core, SQLite), `LyriousRepository`, API models.
- `Lyrious.Api`: the server (ASP.NET Core, controllers, SignalR hub `LyriousHub`). It also serves
  the web app under `/app/` and the product site (`wwwroot/index.html`) at the root.

## Build and run

- dotnet is `/usr/local/share/dotnet/dotnet` (.NET 10).
- Build what was changed: `dotnet build Lyrious.Desktop` (UI), `dotnet build Lyrious.Web`,
  `dotnet build Lyrious.Api`. A change in UILib must build for both desktop and web.
- "Run desktop": stop the running desktop app, then `dotnet run --project Lyrious.Desktop`.
- "Run web": the AOT Release build, never the Debug WASM interpreter (too slow):
  `dotnet publish Lyrious.Api -c Release`, then run `Lyrious.Api.dll` from the publish folder with
  `Database__SqliteFile=<repo>/Lyrious.Api/lyrious-api.db`. Web app and API share port 5180.
- Test servers for trying things out: a scratch database, a random `Jwt__Key`, never the
  production server (www.lyrious.se) or the user's own login.
- Don't restart the user's running apps or deploy without being asked.

## Code

- C# with nullable on, tabs for indentation, the file's existing namespace style.
- UI is built in C# code, not XAML: controls created and composed in constructors, events wired in
  code (see `HomeView`, `SiteManagerView`, `LyricScrollView`).
- Pages go through `PageLayout.Page(title, content, …, closable, icon)`; icons from `AppIcons`;
  icon buttons via the existing helpers (`IconButton`, `SongsView.LineIcon`).
- Data goes through `ISetlistService` / `IAuthService` (never straight to HTTP from a view); they
  work offline: `EntityCache` keeps the outbox over the snapshot, `SyncEngine` sends it.
- Settings have three scopes: this device, the user, the band (`UserSettings`, `SettingKeys`,
  `ISettingsStore`). Choose the scope deliberately; new user settings go in `UserSettings.Keys`.
- Keyboard shortcuts: tunnel `KeyDown` handlers added on attach and removed on detach; Ctrl and
  ⌘ (Meta) both work.
- Site manager rights only after the elevated login (JWT claim `lyrious:site-manager`, checked by
  `LyriousRepository.IsSiteManager`); the server checks every right, the UI only hides.
- Keep changes small and in the style of the code around them; no new packages without asking.

## Comments

- Every type and member gets an XML doc comment that says what it is and does *for the user*, in
  plain words ("The songs of the band's songbook, newest first"), not how it's implemented.
- Inline comments explain why, not what. Write full sentences in the same plain style.

## Texts and languages

- Every text the user sees goes through `L.T("English text", values…)` or
  `L.Plural(count, "{0} song", "{0} songs")`. Never a bare string in the UI.
- Add the Swedish translation for every new text in `Lyrious.UILib/Localization/Swedish.cs`,
  keyed by the exact English text. Check for duplicate keys (a duplicate silently overwrites).
- Write texts in plain, friendly words; "…" (one character) for actions that open more.

## Design

- Dark theme is the default; everything must also work in light (theme setting: dark, light,
  computer). Colours come from theme resources (`LyriousTheme.*`, bound with
  `GetResourceObservable` / `DynamicResource`), never hard-coded per theme.
- Highlighted (accent) buttons use the theme's contrast colour for their text and icons
  (`ColorSettings.SetOnHighlight`); icons bind their stroke to `TextElement.Foreground`.
- Drawers: menu on the left, setlist on the right, opened by buttons in the top corners (44 px
  edge columns); they follow the theme (`DrawerBrush`, `DrawerItemBrush`).
- Lists are compact: little padding, details shown inline in the row, one row open at a time,
  the opened row scrolled into view. Actions sit right-aligned on the item's name line.
- Editing in place: click a name to edit it; Enter or a ✓ button (shown only when the text
  changed) saves; losing focus cancels.
- Settings take effect and are saved as soon as they're changed; Escape never puts a value back.
  Dropdowns in settings are `SettingComboBox`.
- A setting's explanation is the tooltip of its name (`SettingsView.Setting` / `SettingName`), not text
  under it; a section's note is the tooltip of its heading.
- Text buttons for actions, with a tooltip when an icon is used instead; tooltips on anything
  whose meaning isn't obvious.
- Lyrics page: the first line starts below the toolbar; while playing (only then) the lyrics can
  over-scroll so the top and bottom reach the middle of the view.
- Touch: sideways swipes change song (the next slides in over, the current slides away to show
  the previous); a drag locks to scrolling or swiping by its first 5 px until released.
- Works at phone width; nothing important only on hover.

## Terms

- "Longclick": press and hold for 1 second; the long action replaces the normal click.
- "Band sync": the band follows the current setlist, song and scrolling live.
- "Focus mode": only the lyrics, without drawers or toolbars.

## Commits

- Commit only when asked. Message: an overall subject line, then bullet all lines in details where `*` lines are notes
  for end-users and `-` lines are technical notes.
