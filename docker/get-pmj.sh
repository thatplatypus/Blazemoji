#!/bin/sh
# Fetches pmj, Packmoji's command, as Packmoji released it, holds it to the digest in a pin
# file, and puts it in a folder under the name pmj.
#   docker/get-pmj.sh <pin file> <output folder> <machine>
#
# The pin file is a copy of the pmj-<version>.sha256 a release of Packmoji carries: a line
# for each machine, a digest and the name of the file. It is kept in this repository, so
# what is fetched is held to bytes that were looked at once, and not to whatever the release
# serves on the day. The machine is linux-x64, osx-arm64 or win-x64, the three Packmoji
# releases for.
#
# Run by the Dockerfile for the images and by scripts/get-pmj.sh at a desk. Needs curl, and
# sha256sum or shasum.
set -eu

pin="$1"
out="$2"
machine="$3"

case "$machine" in
  linux-x64 | osx-arm64) suffix="" ;;
  win-x64) suffix=".exe" ;;
  *)
    echo "Packmoji releases pmj for linux-x64, osx-arm64 and win-x64, and not for $machine." >&2
    exit 1
    ;;
esac

# A pin that a checkout or an editor gave Windows line endings is the same pin.
lines="$(tr -d '\r' < "$pin" | grep -E "^[0-9a-f]{64}  pmj-[0-9A-Za-z.+-]+-$machine$suffix\$" || true)"
if [ -z "$lines" ] || [ "$(printf '%s\n' "$lines" | wc -l)" -ne 1 ]; then
  echo "$pin must hold one line for $machine: a SHA-256, two spaces, and the name of pmj's file." >&2
  exit 1
fi

digest="${lines%% *}"
file="${lines##* }"
version="${file#pmj-}"
version="${version%-"$machine$suffix"}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
curl -fsSL "https://github.com/thatplatypus/Packmoji/releases/download/v$version/$file" -o "$work/$file"

if command -v sha256sum > /dev/null 2>&1; then
  fetched="$(sha256sum "$work/$file" | cut -d ' ' -f 1)"
else
  fetched="$(shasum -a 256 "$work/$file" | cut -d ' ' -f 1)"
fi
if [ "$fetched" != "$digest" ]; then
  echo "What was fetched as $file is not the program $pin names. Its SHA-256 is $fetched." >&2
  exit 1
fi

mkdir -p "$out"
mv "$work/$file" "$out/pmj$suffix"
chmod +x "$out/pmj$suffix"

# Fails here, and not at the first project that has packages, if what was fetched cannot
# start on this machine or is not the version its name says.
said="$("$out/pmj$suffix" --version | tr -d '\r')"
case "$said" in
  "$version" | "$version+"*) ;;
  *)
    echo "pmj says it is $said, and $pin names $version." >&2
    exit 1
    ;;
esac
echo "pmj $said"
