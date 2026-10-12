#!/bin/sh
# Builds pmj, Packmoji's command, for this machine from the commit in packmoji.pin, and puts
# it in Blazemoji.Toolchain/pmj, from where a build copies it beside the web app and the
# desktop app. With it, a project that has a packmoji.json has its packages fetched when
# either app is run from this checkout. Without it such a project says that this copy of
# Blazemoji has no Packmoji, and every other project works as it always has.
#
#   scripts/get-pmj.sh
#
# It needs the .NET SDK this repository builds with, and the network once, for Packmoji's
# source and for what building it restores. What it builds runs on that SDK's runtime, so
# it is for a desk and not for a packed app, which has no .NET beside it.
set -eu
cd "$(dirname "$0")/.."

rm -rf Blazemoji.Toolchain/pmj
docker/build-pmj.sh packmoji.pin "$(pwd)/Blazemoji.Toolchain/pmj"
echo "pmj is in Blazemoji.Toolchain/pmj. Build an app again to have it copied beside it."
