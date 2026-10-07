#!/bin/sh
# Runs inside the toolchain container. Builds the Grapevine package the way its own build
# script does, then checks that its Todo sample still compiles and links against the result.
#   /compiler   Blazemoji.Toolchain.Local: the compiler, its headers and the stock packages (read-only)
#   /gv         a Grapevine checkout: packages/ and app/ (read-only)
#   /out        package/ gets libgrapevine.a and the interface file, docs/ gets documentation.json
set -eu

EC=/compiler/emojicodec/emojicodec
STOCK=/compiler/packages
work=/tmp/grapevine-build
mkdir -p "$work/built" "$work/packages" /out/package /out/docs
cp -r /gv/packages "$work/src"

# The package: one Emojicode object and one C++ object in an archive, plus the interface
# file other programs are compiled against. -r also writes the documentation report.
cd "$work/src/grapevine"
"$EC" grapevine.🍇 -S "$STOCK" -p grapevine -c -o "$work/built/grapevine.o" -i "$work/built/🏛" -r
c++ -std=c++17 -O2 -c native/net.cpp -I /compiler/emojicodec/include -o "$work/built/net.o"
ar rcs "$work/built/libgrapevine.a" "$work/built/grapevine.o" "$work/built/net.o"

report="$work/built/documentation.json"
[ -f "$report" ] || report="$work/src/grapevine/documentation.json"

# The sample, compiled and linked exactly as the toolchain service will do it.
cp -r "$STOCK"/. "$work/packages/"
mkdir -p "$work/packages/grapevine" "$work/app"
cp "$work/built/libgrapevine.a" "$work/built/🏛" "$work/packages/grapevine/"
cp /gv/app/*.🍇 "$work/app/"
cd "$work/app"
"$EC" main.🍇 -c -o "$work/app/program.o" -S "$work/packages"
c++ "$work/app/program.o" -Wl,--start-group "$work"/packages/*/lib*.a -Wl,--end-group -lm -lpthread -o "$work/app/program"

cp "$work/built/libgrapevine.a" "$work/built/🏛" /out/package/
cp "$report" /out/docs/documentation.json
echo "package: $(wc -c < /out/package/libgrapevine.a) bytes, interface: $(wc -l < /out/package/🏛) lines, documentation: $(wc -c < /out/docs/documentation.json) bytes"
echo "the Todo sample compiles and links against it"
