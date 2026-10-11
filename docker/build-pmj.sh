#!/bin/sh
# Runs inside an image that has the .NET SDK and curl. Builds pmj, Packmoji's command, from
# the commit a pin file names, into a folder.
#   docker/build-pmj.sh <pin file> <output folder>
#
# Packmoji publishes no binary yet. Its own build makes a native program for one machine;
# this one makes a program that runs on the .NET runtime, which every image here already has
# and which is the same code. When Packmoji publishes releases, the pin becomes a version
# and a digest, and this script a download.
#
# A warning in Packmoji's code that a newer SDK has learned to give is not this build's to
# fail on: the commit is pinned, and its own checks passed where it was written.
set -eu

pin="$1"
out="$2"

commit="$(tr -d '[:space:]' < "$pin")"
case "$commit" in
  *[!0-9a-f]* | "")
    echo "$pin must hold one commit of Packmoji, as its full hexadecimal name" >&2
    exit 1
    ;;
esac
if [ "${#commit}" -ne 40 ]; then
  echo "$pin must hold one commit of Packmoji, as its full hexadecimal name" >&2
  exit 1
fi

src="$(mktemp -d)"
curl -fsSL "https://github.com/thatplatypus/Packmoji/archive/$commit.tar.gz" | tar -xz -C "$src" --strip-components=1
dotnet publish "$src/src/Packmoji.Cli" -c Release -r linux-x64 --self-contained false \
  -p:PublishAot=false -p:TreatWarningsAsErrors=false -o "$out"
rm -rf "$src"

# Fails the build here, and not at the first compile, if what was built cannot start.
"$out/pmj" --version
