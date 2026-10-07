#!/bin/sh
# Runs inside the toolchain container. For each stock package, compiles its source with the
# compiler's -r flag and copies the documentation.json it writes to /out/<package>/.
#   /compiler   the emojicodec binary and its packages folder (read-only)
#   /pkgsrc     Emojicode package sources, one folder per package (read-only)
#   /out        where the reports go
set -eu

for package in s files json sockets testtube; do
  work="/tmp/package-docs/$package"
  mkdir -p "$work" "/out/$package"
  cp -r "/pkgsrc/$package/." "$work/"
  cd "$work"
  /compiler/emojicodec/emojicodec "$package.🍇" -S /compiler/packages -p "$package" -c -o "$work/$package.o" -r
  cp documentation.json "/out/$package/documentation.json"
  echo "$package: $(wc -c < documentation.json) bytes"
done
