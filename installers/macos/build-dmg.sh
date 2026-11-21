#!/usr/bin/env bash
# Builds the macOS installer of the desktop app: Lyrious.app (self-contained, so no .NET is needed
# on the Mac) in a disk image with a shortcut to Applications to drag it to.
#
#   installers/macos/build-dmg.sh [arm64|x64 ...]     (default: both)
#
# Out: installers/out/Lyrious-<version>-macos-<arch>.dmg. The version is the one the build takes
# from git (the last tag and the commits since it, see Directory.Build.props).
#
# Signing: unsigned unless MACOS_SIGN_IDENTITY names a "Developer ID Application" certificate in
# the keychain. Unsigned, macOS warns that the developer can't be verified (open it with
# right-click → Open). For a warning-free download it must also be notarized (see the README).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
project="$root/Lyrious.Desktop/Lyrious.Desktop.csproj"
out="$root/installers/out"
work="$root/installers/macos/.work"
dotnet="${DOTNET:-dotnet}"
bundle_id="se.lyrious.desktop"
archs=("${@:-arm64 x64}")
read -r -a archs <<< "${archs[*]}"

# The version: 0.1.0.7 for 7 commits after the tag 0.1.0 (as Directory.Build.props makes it).
describe="$(git -C "$root" describe --tags --long --match '*[0-9]*' 2>/dev/null || echo '0.0.0-0-g0')"
tag="$(sed -E 's/^v?([0-9]+(\.[0-9]+){0,2}).*/\1/' <<< "$describe")"
count="$(sed -E 's/^.*-([0-9]+)-g[0-9a-f]+$/\1/' <<< "$describe")"
while [[ "$(tr -cd '.' <<< "$tag" | wc -c | tr -d ' ')" -lt 2 ]]; do tag="$tag.0"; done
version="$tag.$count"
echo "Lyrious $version"

mkdir -p "$out"
for arch in "${archs[@]}"; do
  rid="osx-$arch"
  publish="$work/publish-$arch"
  app="$work/$arch/Lyrious.app"
  rm -rf "$publish" "$work/$arch"

  echo "== $rid: publishing"
  "$dotnet" publish "$project" -c Release -r "$rid" --self-contained true \
    -p:UseAppHost=true -p:PublishSingleFile=false -o "$publish" -v quiet -nologo

  echo "== $rid: Lyrious.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$publish/." "$app/Contents/MacOS/"
  cp "$root/installers/icons/Lyrious.icns" "$app/Contents/Resources/Lyrious.icns"
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>Lyrious</string>
	<key>CFBundleDisplayName</key>
	<string>Lyrious</string>
	<key>CFBundleIdentifier</key>
	<string>$bundle_id</string>
	<key>CFBundleExecutable</key>
	<string>Lyrious.Desktop</string>
	<key>CFBundleIconFile</key>
	<string>Lyrious.icns</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>$tag</string>
	<key>CFBundleVersion</key>
	<string>$version</string>
	<key>LSMinimumSystemVersion</key>
	<string>12.0</string>
	<key>LSApplicationCategoryType</key>
	<string>public.app-category.music</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSPrincipalClass</key>
	<string>NSApplication</string>
</dict>
</plist>
PLIST
  chmod +x "$app/Contents/MacOS/Lyrious.Desktop"

  if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
    echo "== $rid: signing as $MACOS_SIGN_IDENTITY"
    codesign --force --deep --options runtime --timestamp --sign "$MACOS_SIGN_IDENTITY" "$app"
  else
    # Ad hoc: Apple Silicon only runs signed code, and an ad hoc signature is enough on this Mac.
    codesign --force --deep --sign - "$app"
  fi

  echo "== $rid: disk image"
  stage="$work/$arch/dmg"
  mkdir -p "$stage"
  cp -R "$app" "$stage/"
  ln -s /Applications "$stage/Applications"
  dmg="$out/Lyrious-$version-macos-$arch.dmg"
  rm -f "$dmg"
  hdiutil create -volname "Lyrious $tag" -srcfolder "$stage" -ov -format UDZO "$dmg" >/dev/null
  if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
    codesign --force --timestamp --sign "$MACOS_SIGN_IDENTITY" "$dmg"
  fi
  echo "   $dmg"
done

rm -rf "$work"
