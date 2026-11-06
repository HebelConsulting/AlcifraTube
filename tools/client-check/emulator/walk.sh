#!/usr/bin/env bash
# The walk once the emulator is up (core #2142, item 3): trust the check's edge inside it, then run the app's own
# integration test on the device; a red run dumps the emulator's log for the app and its player. One script, because the
# emulator runner's `script` input runs EACH LINE as its own shell: a `cd` or a line continuation there is lost.
#
#   tools/client-check/emulator/walk.sh <root.crt> <video host>      # from the repository root, app/ checked out
set -uo pipefail
ca=$1; host=$2
tools/client-check/emulator/trust-edge.sh "$ca" "$host" || exit 1
cd app
flutter test integration_test/alcifra_walk_test.dart -d emulator-5554 --flavor stable \
  --dart-define-from-file=env-stable.json --dart-define=logLevel=0 --dart-define="ALCIFRA_CHECK_HOST=$host" --timeout 20m \
  || { echo "::group::logcat"; adb logcat -d | grep -E "flutter|ExoPlayer|MediaCodec|AndroidRuntime|Conscrypt" | tail -300; echo "::endgroup::"; exit 1; }
