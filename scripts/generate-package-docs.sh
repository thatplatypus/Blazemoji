#!/bin/sh
# Regenerates the package documentation the toolchain service serves, by compiling each stock
# package from the Emojicode source at the release tag with the compiler's -r flag.
# The output is committed; run this only when the compiler or the tag changes.
set -eu
cd "$(dirname "$0")/.."

TAG="${EMOJICODE_TAG:-v1.0-beta.2}"
WORK="$(mktemp -d)"
IMAGE=blazemoji-sdk-toolchain

curl -fsSL "https://github.com/emojicode/emojicode/archive/refs/tags/$TAG.tar.gz" | tar -xz -C "$WORK" --strip-components=1
docker build -q -f Blazemoji/Dockerfile --target sdk-toolchain -t "$IMAGE" . > /dev/null

docker run --rm --platform linux/amd64 \
  -v "$PWD/Blazemoji":/compiler:ro \
  -v "$WORK":/pkgsrc:ro \
  -v "$PWD/Blazemoji.Toolchain.Service/package-docs":/out \
  -v "$PWD/docker/generate-package-docs.sh":/generate.sh:ro \
  "$IMAGE" sh /generate.sh

echo "Sources are in $WORK and can be deleted."
