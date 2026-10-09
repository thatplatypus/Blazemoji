#!/bin/sh
# Compiles the app's TypeScript to the JavaScript it serves. The output is committed, so this
# only needs running after a .ts file under Blazemoji.Components/Scripts or
# Blazemoji.Desktop/Scripts changes. Needs Node.
set -eu
cd "$(dirname "$0")/.."

npx --yes --package typescript@5.9.3 tsc \
  --target ES2020 --module ES2020 --strict --newLine lf \
  --outDir Blazemoji.Components/wwwroot/js \
  Blazemoji.Components/Scripts/emojicodeLanguage.ts \
  Blazemoji.Components/Scripts/clipboard.ts \
  Blazemoji.Components/Scripts/splitView.ts \
  Blazemoji.Components/Scripts/programInput.ts

echo "wrote Blazemoji.Components/wwwroot/js/emojicodeLanguage.js, clipboard.js, splitView.js and programInput.js"

npx --yes --package typescript@5.9.3 tsc \
  --target ES2020 --module ES2020 --strict --newLine lf --lib ES2020,DOM,DOM.Iterable \
  --outDir Blazemoji.Desktop/wwwroot/js \
  Blazemoji.Desktop/Scripts/smoke.ts

echo "wrote Blazemoji.Desktop/wwwroot/js/smoke.js"
