#!/bin/sh
# Publishes the server with the web app (Release, AOT) and sends it to Railway, with this release's
# notes: the subjects of the commits since the last release, as a release note that the server adds
# when it starts (unpublished, for a site manager to check and publish on the About page; see
# Lyrious.Api/Services/ReleaseNoteImport.cs).
#
# Before the first run: `railway login`, then in this folder `railway init` (or `railway link`),
# add a volume mounted at /data, and set the variable Jwt__Key to a long random secret.
#
#   deploy/railway.sh            publish, write the release note, and send it to Railway
#   deploy/railway.sh --notes    only show the release text (nothing is built or sent)
set -e
cd "$(dirname "$0")"
root="$(cd .. && pwd)"

# The version and branch, as the build takes them (see Directory.Build.props): the last tag and the
# commits since it, e.g. 0.1.3.2.
describe="$(git -C "$root" describe --tags --long --match '*[0-9]*' 2>/dev/null || echo '0.0.0-0-g0')"
tag="$(git -C "$root" describe --tags --abbrev=0 --match '*[0-9]*' 2>/dev/null || true)"
count="$(echo "$describe" | sed -E 's/^.*-([0-9]+)-g[0-9a-f]+$/\1/')"
version="$(echo "${tag:-0.0.0}" | sed -E 's/^v?([0-9]+(\.[0-9]+){0,2}).*/\1/')"
while [ "$(echo "$version" | tr -cd '.' | wc -c | tr -d ' ')" -lt 2 ]; do version="$version.0"; done
version="$version.$count"
branch="$(git -C "$root" rev-parse --abbrev-ref HEAD)"
[ "$branch" = "HEAD" ] && branch="detached"

# The last release: on a release's tag itself (no commits since), the tag before it; else that tag.
if [ "$count" = "0" ] && [ -n "$tag" ]; then
  last="$(git -C "$root" describe --tags --abbrev=0 --match '*[0-9]*' "$tag^" 2>/dev/null || true)"
else
  last="$tag"
fi
range="${last:+$last..}HEAD"

# The release text: one line per commit subject (no merges, no repeats, oldest first).
notes="$(git -C "$root" log "$range" --no-merges --reverse --format='%s' | awk 'NF && !seen[$0]++')"
echo "Lyrious $version on $branch, changes since ${last:-the first commit}:"
echo "$notes" | sed 's/^/  - /'
[ "${1:-}" = "--notes" ] && exit 0

rm -rf app
dotnet publish ../Lyrious.Api -c Release -o app
# Local files that mustn't go along: this machine's database.
rm -f app/*.db

# This version's release note, added by the server when it starts.
printf '%s\n' "$notes" | python3 -I -c '
import json, sys, datetime
version, branch, out = sys.argv[1:4]
json.dump({"version": version, "branch": branch, "text": sys.stdin.read().strip(),
           "created": datetime.datetime.now().isoformat(timespec="seconds")},
          open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
' "$version" "$branch" app/release-note.json

railway up
