#!/bin/sh
# Compiles the app's TypeScript to the JavaScript it serves. The output is committed, so this
# only needs running after a .ts file under Blazemoji/Scripts changes. Needs Node.
set -eu
cd "$(dirname "$0")/.."

npx --yes --package typescript@5.9.3 tsc \
  --target ES2020 --module ES2020 --strict --newLine lf \
  --outDir Blazemoji/wwwroot/js \
  Blazemoji/Scripts/emojicodeLanguage.ts

echo "wrote Blazemoji/wwwroot/js/emojicodeLanguage.js"
