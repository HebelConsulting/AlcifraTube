#!/usr/bin/env bash
# Lets the app in the emulator reach the check's edge (core #2142, item 3): the edge authority's root goes into the
# emulator's SYSTEM trust store, which the app's HTTP client and the platform's media player both read (a user-added
# certificate is trusted by neither, since the app targets a recent API level), and the video host's name is pointed at
# the runner, which the emulator sees as 10.0.2.2. The app then runs the same code path a release build runs: TLS to the
# host it was given, no plain-HTTP define.
#
# Root through adb (a google_apis image allows it; a Play image does not), then a tmpfs over the store's directory and a
# bind mount over the hosts file: nothing is written to the system image, so no verity change and no reboot. The store
# is a directory of hashed files on API 33 and below; API 34 moved it into an APEX, which this script does not handle.
#
#   tools/client-check/emulator/trust-edge.sh <root.crt> <video host>
set -euo pipefail
ca=$1; host=$2
store=/system/etc/security/cacerts

adb wait-for-device
adb root
for _ in $(seq 1 30); do
  adb wait-for-device
  [ "$(adb shell id -u 2>/dev/null | tr -d '\r')" = 0 ] && break
  sleep 1
done
[ "$(adb shell id -u | tr -d '\r')" = 0 ] || { echo "trust-edge: adb did not become root" >&2; exit 1; }

name="$(openssl x509 -noout -subject_hash_old -in "$ca").0"
adb push "$ca" "/data/local/tmp/$name" > /dev/null
adb shell "set -e
  rm -rf /data/local/tmp/cacerts && mkdir -m 700 /data/local/tmp/cacerts
  cp $store/* /data/local/tmp/cacerts/
  mount -t tmpfs tmpfs $store
  mv /data/local/tmp/cacerts/* $store/
  mv /data/local/tmp/$name $store/
  chown root:root $store/*
  chmod 644 $store/*
  chcon u:object_r:system_file:s0 $store/*
  printf '10.0.2.2 %s\n' '$host' > /data/local/tmp/hosts
  cat /system/etc/hosts >> /data/local/tmp/hosts
  chmod 644 /data/local/tmp/hosts
  chcon u:object_r:system_file:s0 /data/local/tmp/hosts
  mount -o bind /data/local/tmp/hosts /system/etc/hosts"

adb shell "test -s $store/$name" || { echo "trust-edge: the root is not in the system store" >&2; exit 1; }
echo "trust-edge: $name in $store ($(adb shell ls "$store" | wc -l | tr -d ' ') roots), $host → 10.0.2.2"
adb shell cat /system/etc/hosts
