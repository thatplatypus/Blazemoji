#!/bin/sh
# Cuts every icon file the two hosts use from the drawings under art/. The output is
# committed, so this only needs running after a drawing changes. Needs rsvg-convert (librsvg)
# and magick (ImageMagick): "brew install librsvg imagemagick". The macOS icon also needs
# iconutil, which only a Mac has; anywhere else it is left as it was.
#
#   art/flame.svg      the flame, used at every size but the smallest
#   art/flame-16.svg   the same flame fitted to a 16 pixel grid
#   art/tile.svg       the rounded square macOS expects an icon to be drawn on
#
# Windows and Linux get the flame alone. So does a browser tab.
set -eu
cd "$(dirname "$0")/.."

for tool in rsvg-convert magick; do
  command -v "$tool" >/dev/null || { echo "build-icons: $tool was not found" >&2; exit 1; }
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# flame <pixels>: the flame alone on nothing, as a file whose name is printed
flame() {
  if [ "$1" -eq 16 ]; then drawing=art/flame-16.svg; else drawing=art/flame.svg; fi
  rsvg-convert -w "$1" -h "$1" "$drawing" -o "$work/flame-$1.png"
  echo "$work/flame-$1.png"
}

# on_the_tile <pixels> <file>: the flame on the macOS tile. The flame takes more of a small
# tile, where it would otherwise be a few pixels of orange.
on_the_tile() {
  if [ "$1" -le 32 ]; then share=68; else share=56; fi
  rsvg-convert -w "$1" -h "$1" art/tile.svg -o "$work/tile.png"
  rsvg-convert -w $(( $1 * share / 100 )) -h $(( $1 * share / 100 )) art/flame.svg -o "$work/on-tile.png"
  magick "$work/tile.png" "$work/on-tile.png" -gravity center -composite -strip "$2"
}

# many_sizes <file.ico> <pixels>...: one icon file holding each size
many_sizes() {
  ico=$1; shift
  sizes=""
  for pixels in "$@"; do sizes="$sizes $(flame "$pixels")"; done
  # shellcheck disable=SC2086
  magick $sizes -strip "$ico"
}

desktop=Blazemoji.Desktop/wwwroot
web=Blazemoji/wwwroot

many_sizes "$desktop/logo.ico" 16 20 24 32 40 48 64 256
cp "$(flame 512)" "$desktop/logo.png"

many_sizes "$web/favicon.ico" 16 32 48
# Where a phone's home screen or Safari's favourites keep a page. They cut their own corners
# and paint nothing as black, so this one is a full square of the tile's colours.
magick -size 180x180 gradient:'#303037-#18181B' "$(flame 126)" -gravity center -composite -strip "$web/apple-touch-icon.png"

if command -v iconutil >/dev/null; then
  set_of_sizes="$work/logo.iconset"
  mkdir "$set_of_sizes"
  for points in 16 32 128 256 512; do
    on_the_tile "$points" "$set_of_sizes/icon_${points}x${points}.png"
    on_the_tile $(( points * 2 )) "$set_of_sizes/icon_${points}x${points}@2x.png"
  done
  iconutil -c icns "$set_of_sizes" -o "$desktop/logo.icns"
else
  echo "build-icons: no iconutil here, so $desktop/logo.icns was left as it was" >&2
fi

echo "wrote $desktop/logo.ico, logo.png and logo.icns, and $web/favicon.ico and apple-touch-icon.png"
