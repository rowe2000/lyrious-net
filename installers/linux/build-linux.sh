#!/usr/bin/env bash
# Builds the Linux packages of the desktop app (self-contained, so no .NET is needed):
#
#   installers/linux/build-linux.sh [x64] [arm64]     (default: x64)
#
# Out, in installers/out/:
#   Lyrious-<version>-linux-<arch>.tar.gz     always (unpack anywhere, run ./lyrious)
#   lyrious_<version>_<amd64|arm64>.deb       when dpkg-deb is there (Debian, Ubuntu, Mint, …)
#   Lyrious-<version>-linux-x86_64.AppImage   x64, when appimagetool is there (or APPIMAGETOOL
#                                             names it); one file that runs on most distributions
# The version is the one the build takes from git (see Directory.Build.props).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
project="$root/Lyrious.Desktop/Lyrious.Desktop.csproj"
out="$root/installers/out"
work="$root/installers/linux/.work"
dotnet="${DOTNET:-dotnet}"
archs=("${@:-x64}")
read -r -a archs <<< "${archs[*]}"

describe="$(git -C "$root" describe --tags --long --match '*[0-9]*' 2>/dev/null || echo '0.0.0-0-g0')"
tag="$(sed -E 's/^v?([0-9]+(\.[0-9]+){0,2}).*/\1/' <<< "$describe")"
count="$(sed -E 's/^.*-([0-9]+)-g[0-9a-f]+$/\1/' <<< "$describe")"
while [[ "$(tr -cd '.' <<< "$tag" | wc -c | tr -d ' ')" -lt 2 ]]; do tag="$tag.0"; done
version="$tag.$count"
echo "Lyrious $version"

appimagetool="${APPIMAGETOOL:-$(command -v appimagetool || true)}"

# The menu entry, the same in every package (Exec is set per package). It also takes lyrious://
# links (%u), so the web app can open the installed app. Named se.lyrious.Lyrious.desktop
# (reverse DNS, as freedesktop recommends; "lyrious.desktop" would also clash with the program
# Lyrious.Desktop where names ignore case, e.g. when built on a Mac).
entry_name="se.lyrious.Lyrious.desktop"
desktop_entry() {
  cat <<ENTRY
[Desktop Entry]
Type=Application
Name=Lyrious
GenericName=Songs and setlists for bands
Comment=Songs, setlists and lyrics for your band, in sync
Exec=$1 %u
Icon=lyrious
Terminal=false
Categories=AudioVideo;Audio;Music;
StartupWMClass=Lyrious.Desktop
MimeType=x-scheme-handler/lyrious;
ENTRY
}

mkdir -p "$out"
for arch in "${archs[@]}"; do
  rid="linux-$arch"
  publish="$work/publish-$arch"
  rm -rf "$publish" "$work/$arch"
  mkdir -p "$work/$arch"

  echo "== $rid: publishing"
  "$dotnet" publish "$project" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=false -o "$publish" -v quiet -nologo
  chmod +x "$publish/Lyrious.Desktop"

  # tar.gz: the app in a folder, with a start script and the icon and menu entry beside it.
  echo "== $rid: tar.gz"
  folder="$work/$arch/Lyrious-$version"
  mkdir -p "$folder"
  cp -R "$publish/." "$folder/"
  cp "$root/Lyrious.Web/wwwroot/icons/icon-512.png" "$folder/lyrious.png"
  printf '#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/Lyrious.Desktop" "$@"\n' > "$folder/lyrious"
  chmod +x "$folder/lyrious"
  desktop_entry "lyrious" > "$folder/$entry_name"
  tar -C "$work/$arch" -czf "$out/Lyrious-$version-linux-$arch.tar.gz" "Lyrious-$version"
  echo "   $out/Lyrious-$version-linux-$arch.tar.gz"

  # .deb: in /opt/lyrious, the command lyrious, a menu entry and the icon.
  if command -v dpkg-deb >/dev/null; then
    echo "== $rid: .deb"
    deb_arch="$([[ $arch == arm64 ]] && echo arm64 || echo amd64)"
    pkg="$work/$arch/deb"
    mkdir -p "$pkg/DEBIAN" "$pkg/opt/lyrious" "$pkg/usr/bin" "$pkg/usr/share/applications" "$pkg/usr/share/icons/hicolor/512x512/apps"
    cp -R "$publish/." "$pkg/opt/lyrious/"
    ln -s /opt/lyrious/Lyrious.Desktop "$pkg/usr/bin/lyrious"
    desktop_entry "/opt/lyrious/Lyrious.Desktop" > "$pkg/usr/share/applications/$entry_name"
    cp "$root/Lyrious.Web/wwwroot/icons/icon-512.png" "$pkg/usr/share/icons/hicolor/512x512/apps/lyrious.png"
    cat > "$pkg/DEBIAN/control" <<CONTROL
Package: lyrious
Version: $version
Architecture: $deb_arch
Maintainer: Lyrious <no-reply@lyrious.se>
Homepage: https://lyrious.se
Section: sound
Priority: optional
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libssl3 | libssl1.1, libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66, libfontconfig1, libx11-6, libice6, libsm6
Recommends: libsecret-tools, xdg-utils
Description: Songs, setlists and lyrics for your band, in sync
 Lyrious keeps your band's songs, setlists and lyrics in one place, on every
 member's screen at once.
CONTROL
    deb="$out/lyrious_${version}_${deb_arch}.deb"
    dpkg-deb --build --root-owner-group "$pkg" "$deb" >/dev/null
    echo "   $deb"
  fi

  # AppImage (x64): the app, its menu entry and icon in one runnable file.
  if [[ $arch == x64 && -n "$appimagetool" ]]; then
    echo "== $rid: AppImage"
    appdir="$work/$arch/Lyrious.AppDir"
    mkdir -p "$appdir/usr/bin"
    cp -R "$publish/." "$appdir/usr/bin/"
    printf '#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/usr/bin/Lyrious.Desktop" "$@"\n' > "$appdir/AppRun"
    chmod +x "$appdir/AppRun"
    desktop_entry "Lyrious.Desktop" > "$appdir/$entry_name"
    cp "$root/Lyrious.Web/wwwroot/icons/icon-512.png" "$appdir/lyrious.png"
    image="$out/Lyrious-$version-linux-x86_64.AppImage"
    ARCH=x86_64 "$appimagetool" "$appdir" "$image" >/dev/null
    echo "   $image"
  fi
done

rm -rf "$work"
