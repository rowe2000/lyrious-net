# Desktop app installers

The desktop app (Lyrious.Desktop) is published self-contained, so the computer needs no .NET. The
version is the one the build takes from git: the last tag and the commits since it, e.g. `0.1.2.7`
(see `Directory.Build.props`). Everything is built into `installers/out/` (ignored by git).

On Linux the app keeps saved passwords in the desktop's keyring through `secret-tool` (package
libsecret-tools), opens sound files in the system's player (no speed or pitch there), and has no
MIDI output (the MIDI library doesn't support Linux).

| | Built by | Result |
|---|---|---|
| macOS | `installers/macos/build-dmg.sh [arm64] [x64]` (on a Mac) | `Lyrious-<version>-macos-arm64.dmg` (Apple Silicon), `…-x64.dmg` (Intel) |
| Windows | `installers\windows\build-installer.ps1` (on Windows, with [Inno Setup 6](https://jrsoftware.org/isinfo.php)) | `Lyrious-<version>-windows-x64-setup.exe`, `…-portable.zip` |
| Linux | `installers/linux/build-linux.sh [x64] [arm64]` (the .deb on a system with `dpkg-deb`, the AppImage with `appimagetool`; the .tar.gz anywhere) | `lyrious_<version>_amd64.deb` / `_arm64.deb`, `Lyrious-<version>-linux-x86_64.AppImage`, `Lyrious-<version>-linux-x64.tar.gz` / `-arm64.tar.gz` |

**On GitHub:** `.github/workflows/desktop-installers.yml` builds all of them. Push a tag (e.g.
`git tag 0.1.3 && git push origin 0.1.3`) and they're attached to its release, or run the workflow
by hand (Actions → Desktop installers) and download them from the run.

## Signing

Linux packages aren't signed (nothing on a desktop system asks for it). Unsigned macOS and Windows
installers work, but warn: macOS says the developer can't be verified (open the app with
right-click → Open the first time), and Windows SmartScreen says the publisher is unknown ("More
info" → "Run anyway"). To sign:

- **macOS:** a *Developer ID Application* certificate (Apple Developer Program). Locally, set
  `MACOS_SIGN_IDENTITY="Developer ID Application: Name (TEAMID)"`. On GitHub, add the secrets
  `MACOS_CERTIFICATE_P12` (base64 of the exported .p12), `MACOS_CERTIFICATE_PASSWORD` and
  `MACOS_SIGN_IDENTITY`. For a download without any warning it must also be notarized:
  `xcrun notarytool submit <dmg> --apple-id … --team-id … --password <app-specific password> --wait`,
  then `xcrun stapler staple <dmg>`.
- **Windows:** a code signing certificate (.pfx). Locally, set `WINDOWS_SIGN_PFX` and
  `WINDOWS_SIGN_PASSWORD`. On GitHub, add the secrets `WINDOWS_CERTIFICATE_PFX` (base64 of the .pfx)
  and `WINDOWS_CERTIFICATE_PASSWORD`.

## Icons

`icons/Lyrious.icns` (macOS) and `icons/Lyrious.ico` (Windows, also the program's own icon) are made
from the logo, `branding/lyrious-logo.svg`, by `branding/build-icons.sh` (as are the web app's and
Android's icons).
