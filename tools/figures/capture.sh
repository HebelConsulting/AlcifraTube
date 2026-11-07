#!/usr/bin/env bash
# The module manual's figures (core #2165): the video host's real pages, from the client check's stack with a few more clips
# filed through `alcifra`, shot by a headless browser. The workflow hands the PNGs over as an artifact; they are committed
# under manual/screenshots and the manual rebuilt, because the committed PDF must be what its source builds.
#
#   tools/figures/capture.sh OUT_DIR      # needs: the stack up and seeded, alcifra, ffmpeg, Chrome
set -euo pipefail
out="$(mkdir -p "$1" && cd "$1" && pwd)"
cd "$(dirname "$0")/../client-check"
# shellcheck disable=SC1091
source seed.env
HOST=${ALCIFRA_CHECK_API:-http://localhost:8080}
SITE=https://tube.test
eval "$(ALCIFRA_CLIENT_SECRET=dev-setup-secret alcifra login --url "$HOST" --client-id demo-setup)"
printf '%s' "Example Videos" | alcifra module settings set tube tube.name --stdin
printf '%s' "Your own videos on your own installation." | alcifra module settings set tube tube.description --stdin

# Moving test patterns, so the grid shows what a grid of videos looks like. Generated here: nothing binary in the repository.
work=$(mktemp -d)
day=1
for source_name in "Colour bars:smptebars" "Mandelbrot:mandelbrot" "Life:life=s=640x360:mold=10:ratio=0.2" "Gradients:gradients=s=640x360" "Test card:pal75bars"; do
  name=${source_name%%:*}; filter=${source_name#*:}
  ffmpeg -loglevel error -y -f lavfi -i "$filter" -t 4 -r 25 -vf "scale=640:360,format=yuv420p" -c:v libx264 "$work/$day.mp4"
  ffmpeg -loglevel error -y -i "$work/$day.mp4" -ss 2 -frames:v 1 "$work/$day.jpg"
  thumbnail="$(alcifra document file "$work/$day.jpg" --parent "$CHANNEL" --name "$name (thumbnail)" --mask "Video Thumbnail")"
  alcifra document file "$work/$day.mp4" --parent "$CHANNEL" --name "$name" --mask Video \
    --field "Description=A short test pattern." --field "Published=true" \
    --field "Published at=2026-11-0${day}T09:00:00Z" --field "Language=en" --field "Duration seconds=4" \
    --field "Thumbnail=$thumbnail" > /dev/null
  day=$((day + 1))
done
rm -rf "$work"

front=$(curl -fsS --cacert "$CHECK_CA" "$SITE/")
channel_path=$(printf '%s' "$front" | grep -oE 'href="/c/[^"]+"' | head -1 | sed -E 's/href="([^"]+)"/\1/')
[ -n "$channel_path" ] || { echo "capture: the front page links no channel" >&2; exit 1; }
shot() {  # file url height
  # The stack's own throwaway authority signs this host, and the page is only being looked at.
  google-chrome --headless=new --no-sandbox --disable-gpu --hide-scrollbars --ignore-certificate-errors \
    --window-size=1280,"$3" --virtual-time-budget=8000 --screenshot="$out/$1" "$2" 2>/dev/null
  test -s "$out/$1" || { echo "capture: no $1" >&2; exit 1; }
  echo "capture: $1 from $2"
}
shot tube-front.png "$SITE/" 900
shot tube-channel.png "$SITE$channel_path" 900
shot tube-watch.png "$SITE/videos/watch/$CLIP" 800
