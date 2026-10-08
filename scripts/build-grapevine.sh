#!/bin/sh
# Builds the Grapevine package from a local Grapevine checkout, at the commit named in
# grapevine.pin, and puts it where the toolchain looks for packages. Also copies Grapevine's
# Todo sample in as a project template.
#
# Nothing this script writes is committed: Grapevine has its own repository, and this one is
# public. The three output folders are in .gitignore. Without them everything else works;
# the package and the template are simply not offered.
#
#   scripts/build-grapevine.sh               build
#   scripts/build-grapevine.sh --if-needed   build only if the pin has changed since the last build
#
# To move to a newer Grapevine: put the commit id in grapevine.pin and run this again.
#   GRAPEVINE_REPO   the checkout to build from (default: ~/Code/grapevine)
set -eu
cd "$(dirname "$0")/.."

PIN="$(tr -d '[:space:]' < grapevine.pin)"
REPO="${GRAPEVINE_REPO:-$HOME/Code/grapevine}"
PACKAGE=Blazemoji.Toolchain.Local/packages/grapevine
DOCS=Blazemoji.Toolchain.Service/package-docs/grapevine
TEMPLATE=Blazemoji.Core/Emojicode/Templates/grapevine-todo
IMAGE=blazemoji-sdk-toolchain

if [ "${1:-}" = "--if-needed" ] && [ -f "$PACKAGE/.commit" ] && [ "$(cat "$PACKAGE/.commit")" = "$PIN" ]; then
  exit 0
fi

if ! git -C "$REPO" cat-file -e "$PIN^{commit}" 2> /dev/null; then
  echo "Commit $PIN (from grapevine.pin) is not in $REPO. Set GRAPEVINE_REPO or fix the pin." >&2
  exit 1
fi

# The commit, not the working tree: whatever is being edited in the checkout is left out.
WORK="$(mktemp -d)"
mkdir -p "$WORK/src" "$WORK/out"
git -C "$REPO" archive "$PIN" packages app | tar -x -C "$WORK/src"

docker build -q -f Blazemoji/Dockerfile --target sdk-toolchain -t "$IMAGE" . > /dev/null
docker run --rm --platform linux/amd64 \
  -v "$PWD/Blazemoji.Toolchain.Local":/compiler:ro \
  -v "$WORK/src":/gv:ro \
  -v "$WORK/out":/out \
  -v "$PWD/docker/build-grapevine.sh":/build.sh:ro \
  "$IMAGE" sh /build.sh

mkdir -p "$PACKAGE" "$DOCS" "$TEMPLATE"
cp "$WORK/out/package/libgrapevine.a" "$WORK/out/package/🏛" "$PACKAGE/"
cp "$WORK/out/docs/documentation.json" "$DOCS/"
cp "$WORK/src/app/"*.🍇 "$TEMPLATE/"
cat > "$TEMPLATE/template.json" <<JSON
{
  "name": "Grapevine Todo API",
  "description": "Grapevine's sample: a small HTTP API with routes, middleware and a controller. Run it, then send it requests from the Requests tab.",
  "kind": "server",
  "entry": "main.🍇",
  "order": 30
}
JSON
printf '%s\n' "$PIN" > "$PACKAGE/.commit"

echo "Grapevine $PIN is in place. The build's working files are in $WORK and can be deleted."
