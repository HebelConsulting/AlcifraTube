#!/usr/bin/env bash
# Seeds the client check's stack (core #2142): activates the module for the demo tenant, names the video host, and files one
# channel with one clip, through the core's own CLI as an administrator would. Run on the runner, against localhost.
#
#   tools/client-check/seed.sh            # needs: alcifra (dotnet tool), ffmpeg, the stack up on :8080
#
# Output goes through the CLI's own logging; the ids a later step needs are written to seed.env (the channel and the clip).
set -euo pipefail
cd "$(dirname "$0")"
HOST=${ALCIFRA_CHECK_API:-http://localhost:8080}   # the core directly; the edge in front is for the clients
VIDEO_HOST=${ALCIFRA_CHECK_VIDEO_HOST:-tube.test}

# The demo tenant's unattended setup account (core ADR 0949): it manages modules and repositories, which is what a seed
# does. A throwaway secret: the stack lives for one run on one runner and is reachable from nowhere else.
eval "$(ALCIFRA_CLIENT_SECRET=dev-setup-secret alcifra login --url "$HOST" --client-id demo-setup)"

alcifra module activate --module tube
case "$(alcifra module list 2>&1 | grep -w tube || true)" in
  "")             echo "seed: the module is not installed in this core" >&2; exit 1 ;;
  *"not active"*) echo "seed: installed but not active" >&2; exit 1 ;;
esac
printf '%s' "$VIDEO_HOST" | alcifra module settings set tube tube.host --stdin
printf '%s' "Client check" | alcifra module settings set tube tube.name --stdin
printf '%s' "One clip, filed for the weekly client check." | alcifra module settings set tube tube.description --stdin

# One clip and its thumbnail, generated here: nothing binary lives in the repository.
work=$(mktemp -d)
ffmpeg -loglevel error -y -f lavfi -i "testsrc=size=640x360:rate=25" -f lavfi -i "sine=frequency=440:sample_rate=44100" \
  -t 12 -pix_fmt yuv420p -c:v libx264 -c:a aac -shortest "$work/clip.mp4"
ffmpeg -loglevel error -y -i "$work/clip.mp4" -frames:v 1 "$work/clip.jpg"

videos="$(alcifra repository create --name "Videos")"
alcifra acl grant "$videos" --service-account "Module: Tube" --rights see,read-content
channel="$(alcifra document folder "Checks" --parent "$videos" --mask "Tube Channel" --field "Description=The weekly client check.")"
thumbnail="$(alcifra document file "$work/clip.jpg" --parent "$videos" --name "Test pattern (thumbnail)" --mask "Video Thumbnail")"
clip="$(alcifra document file "$work/clip.mp4" --parent "$channel" --name "Test pattern" --mask Video \
  --field "Description=Twelve seconds of a test pattern with a tone." --field "Published=true" \
  --field "Published at=2026-11-05T09:00:00Z" --field "Language=en" --field "Duration seconds=12" \
  --field "Thumbnail=$thumbnail" --field "Tags=check")"
rm -rf "$work"

printf 'CHANNEL=%s\nCLIP=%s\n' "$channel" "$clip" > seed.env
echo "seed: channel $channel, clip $clip"
