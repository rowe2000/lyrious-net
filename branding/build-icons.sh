#!/usr/bin/env bash
# Makes every app icon from the logo, branding/lyrious-logo.svg (the singer, as on the splash
# screen). Run it again after changing the logo:
#
#   branding/build-icons.sh            (needs rsvg-convert: brew install librsvg; and macOS's iconutil)
#
# Writes:
#   branding/lyrious-icon.svg            the singer on a white rounded square (the app icon)
#   branding/lyrious-icon-square.svg     the singer on a full white square, with room around it, for
#                                        systems that round or cut the corners themselves
#   installers/icons/Lyrious.icns, .ico  macOS and Windows (the installers and the program)
#   Lyrious.Web/wwwroot/favicon.ico      the singer alone (transparent), 16 to 256 px
#   Lyrious.Web/wwwroot/icons/           icon-192/512 (installed web app), icon-maskable-512,
#                                        apple-touch-icon (iPhone home screen), singer.svg (the page's splash)
#   Lyrious.Android/Icon.png             Android
set -euo pipefail
# Decimal points in numbers, whatever the computer's language (printf follows it, bc doesn't).
export LC_ALL=C

root="$(cd "$(dirname "$0")/.." && pwd)"
brand="$root/branding"
logo="$brand/lyrious-logo.svg"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The logo's page and its path (with the transform that places it on the page).
viewbox="$(sed -E 's/.*viewBox="([^"]+)".*/\1/' "$logo")"
read -r _ _ logo_w logo_h <<< "$viewbox"
path="$(grep -o '<path[^>]*/>' "$logo")"

# An icon: a square of $size with an optional plate (rounded white square), and the singer
# $share of the plate's width, in the middle.
icon_svg() { # size plate_margin plate_radius background singer_share
  local size=$1 margin=$2 radius=$3 background=$4 share=$5
  local plate=$((size - 2 * margin))
  local w; w=$(echo "$plate * $share" | bc -l)
  local h; h=$(echo "$w * $logo_h / $logo_w" | bc -l)
  local x; x=$(echo "($size - $w) / 2" | bc -l)
  local y; y=$(echo "($size - $h) / 2" | bc -l)
  echo "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"$size\" height=\"$size\" viewBox=\"0 0 $size $size\">"
  if [[ $background != none ]]; then
    echo "  <rect x=\"$margin\" y=\"$margin\" width=\"$plate\" height=\"$plate\" rx=\"$radius\" fill=\"$background\"/>"
  fi
  printf '  <svg x="%.3f" y="%.3f" width="%.3f" height="%.3f" viewBox="%s">%s</svg>\n' "$x" "$y" "$w" "$h" "$viewbox" "$path"
  echo "</svg>"
}

png() { rsvg-convert -w "$2" -h "$2" "$1" -o "$3"; }

# The icons' own SVGs: rounded (the app icon) and square (rounded by the system).
icon_svg 1024 0 224 "#ffffff" 0.78 > "$brand/lyrious-icon.svg"
icon_svg 1024 0 0 "#ffffff" 0.64 > "$brand/lyrious-icon-square.svg"
# macOS: the plate inside Apple's margins (824 of 1024), as other apps' icons are.
icon_svg 1024 100 185 "#ffffff" 0.78 > "$work/macos.svg"
# The favicon: the singer alone, as big as it fits.
icon_svg 1024 0 0 none 0.96 > "$work/favicon.svg"

# macOS
mkdir -p "$work/Lyrious.iconset"
for s in 16 32 128 256 512; do
  png "$work/macos.svg" $s "$work/Lyrious.iconset/icon_${s}x${s}.png"
  png "$work/macos.svg" $((s * 2)) "$work/Lyrious.iconset/icon_${s}x${s}@2x.png"
done
iconutil -c icns "$work/Lyrious.iconset" -o "$root/installers/icons/Lyrious.icns"

# An .ico of PNG images (Windows Vista and later, browsers).
ico() { # svg out sizes...
  local svg=$1 out=$2; shift 2
  for s in "$@"; do png "$svg" "$s" "$work/ico-$s.png"; done
  python3 -I - "$out" "$work" "$@" <<'PY'
import struct, sys, os
out, work, sizes = sys.argv[1], sys.argv[2], [int(s) for s in sys.argv[3:]]
images = [open(os.path.join(work, f"ico-{s}.png"), "rb").read() for s in sizes]
offset = 6 + 16 * len(sizes)
entries = data = b""
for s, image in zip(sizes, images):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(image), offset + len(data))
    data += image
open(out, "wb").write(struct.pack("<HHH", 0, 1, len(sizes)) + entries + data)
PY
}
ico "$brand/lyrious-icon.svg" "$root/installers/icons/Lyrious.ico" 16 24 32 48 64 128 256
ico "$work/favicon.svg" "$root/Lyrious.Web/wwwroot/favicon.ico" 16 24 32 48 64 128 256

# The web app
icons="$root/Lyrious.Web/wwwroot/icons"
png "$brand/lyrious-icon.svg" 192 "$icons/icon-192.png"
png "$brand/lyrious-icon.svg" 512 "$icons/icon-512.png"
png "$brand/lyrious-icon-square.svg" 512 "$icons/icon-maskable-512.png"
png "$brand/lyrious-icon-square.svg" 180 "$icons/apple-touch-icon.png"
cp "$logo" "$icons/singer.svg"

# Android
png "$brand/lyrious-icon.svg" 400 "$root/Lyrious.Android/Icon.png"

echo "Icons made from $logo"
