#!/usr/bin/env bash
# Checks the official PeerTube app out at its newest release tag (core #2142), into ./app, and names the tag on stdout;
# both jobs of the client check read it there. Never vendored: the app is AGPL-3.0, and a copy would stop being the
# client people install.
#
#   tag=$(tools/client-check/app/checkout.sh)
set -euo pipefail
repo=https://framagit.org/framasoft/peertube/mobile-application.git
tag=$(git ls-remote --tags --refs "$repo" | sed 's|.*refs/tags/||' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -1)
[ -n "$tag" ] || { echo "checkout: no release tag at $repo" >&2; exit 1; }
rm -rf app
git clone --quiet --depth 1 --branch "$tag" "$repo" app
echo "$tag"
