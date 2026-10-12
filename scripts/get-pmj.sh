#!/bin/sh
# Fetches pmj, Packmoji's command, for this machine, as the release packmoji.pin names, and
# puts it in Blazemoji.Toolchain/pmj, from where a build or a publish copies it beside the
# web app and the desktop app. With it, a project that has a packmoji.json has its packages
# fetched when either app is run from this checkout. Without it such a project says that
# this copy of Blazemoji has no Packmoji, and every other project works as it always has.
#
#   scripts/get-pmj.sh
#
# It needs the network once, and curl. What it fetches is the program itself and needs no
# .NET, so it is also what a packed desktop app carries.
set -eu
cd "$(dirname "$0")/.."

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) machine=linux-x64 ;;
  Darwin-arm64) machine=osx-arm64 ;;
  MINGW*-x86_64 | MSYS*-x86_64 | CYGWIN*-x86_64) machine=win-x64 ;;
  *)
    echo "Packmoji releases no pmj for this machine ($(uname -s) on $(uname -m))." >&2
    echo "Blazemoji works without one: only a project that has a packmoji.json needs it." >&2
    exit 1
    ;;
esac

# Fetched to one side first, so that a fetch that fails leaves the pmj that was there.
fetched="$(mktemp -d)"
trap 'rm -rf "$fetched"' EXIT
docker/get-pmj.sh packmoji.pin "$fetched" "$machine"

rm -rf Blazemoji.Toolchain/pmj
mkdir -p Blazemoji.Toolchain/pmj
mv "$fetched"/* Blazemoji.Toolchain/pmj/
echo "pmj is in Blazemoji.Toolchain/pmj. Build an app again to have it copied beside it."
