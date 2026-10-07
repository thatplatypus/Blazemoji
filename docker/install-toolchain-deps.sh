#!/bin/sh
# Installs what the prebuilt Emojicode 1.0 beta 2 compiler needs on Ubuntu 24.04:
# a C++ toolchain, because emojicodec shells out to c++ to link, and libtinfo.so.5,
# which Ubuntu stopped packaging after 22.04 (jammy).
#
# The jammy release-pocket build is used because its URL never changes. Builds from
# jammy-updates are deleted from the pool whenever a newer one supersedes them.
set -eu

LIBTINFO5_URL="http://archive.ubuntu.com/ubuntu/pool/universe/n/ncurses/libtinfo5_6.3-2_amd64.deb"
LIBTINFO5_SHA256="d2597b5aec92a930cf549e1b429ad892595813e72ec7814685ea146a9fb715e5"

apt-get update
apt-get install -y --no-install-recommends g++ curl ca-certificates

curl -fsSL "$LIBTINFO5_URL" -o /tmp/libtinfo5.deb
echo "$LIBTINFO5_SHA256  /tmp/libtinfo5.deb" | sha256sum -c -
dpkg -i /tmp/libtinfo5.deb

rm -f /tmp/libtinfo5.deb
rm -rf /var/lib/apt/lists/*
