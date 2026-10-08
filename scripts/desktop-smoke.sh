#!/bin/sh
# Starts the desktop app, has it check itself from inside its own window, and says whether
# it passed. Nothing outside the window can see into it, so this is the desktop host's test:
# the editor loads, takes the page's colours, types emoji pairs from a key and from the
# toolbox, offers completions, goes dark and back, and keeps its project as a folder on disk.
#
#   scripts/desktop-smoke.sh                 the build `dotnet run` would start
#   scripts/desktop-smoke.sh --published     publish for this machine first, and test that
#   scripts/desktop-smoke.sh --compile       also compile and run a program (needs the
#                                            toolchain service: scripts/dev-toolchain.sh)
#
# A window opens for a few seconds and closes itself. The run's projects go to a temporary
# folder, never to your own. The app reports in the words of Hermes's smoke protocol, so
# what judges a Hermes app's smoke run can judge this one: the last line that starts with
# HERMES_SMOKE_RESULT says PASSED or FAILED, and the exit code is 0 only for a pass.
set -eu
cd "$(dirname "$0")/.."

published=0
compile=0
for option in "$@"; do
  case "$option" in
    --published) published=1 ;;
    --compile) compile=1 ;;
    *) echo "Unknown option: $option" >&2; exit 2 ;;
  esac
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ "$published" -eq 1 ]; then
  rid="$(dotnet --info | awk '/^ *RID:/ { print $2; exit }')"
  echo "Publishing for $rid..."
  dotnet publish Blazemoji.Desktop/Blazemoji.Desktop.csproj -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$work/app" > "$work/build.log" 2>&1 \
    || { tail -30 "$work/build.log"; exit 1; }
  app="$work/app/Blazemoji.Desktop"
else
  dotnet build Blazemoji.Desktop/Blazemoji.Desktop.csproj > "$work/build.log" 2>&1 \
    || { tail -30 "$work/build.log"; exit 1; }
  app="$(pwd)/Blazemoji.Desktop/bin/Debug/net10.0/Blazemoji.Desktop"
fi

# Started from somewhere else on purpose: the app must find its own files wherever it is run from.
status=0
(
  cd "$work"
  HERMES_SMOKE_TEST=1 \
  HERMES_SMOKE_TEST_TIMEOUT="${SMOKE_TIMEOUT:-120}" \
  HERMES_SMOKE_TEST_RESULT="$work/result.json" \
  BLAZEMOJI_SMOKE_COMPILE="$compile" \
  Projects__Root="$work/projects" \
    "$app" > "$work/app.log" 2>&1
) || status=$?

grep '^HERMES_SMOKE_' "$work/app.log" || true
if [ "$status" -ne 0 ] || ! grep -q '^HERMES_SMOKE_RESULT: PASSED' "$work/app.log"; then
  echo "The smoke run failed (exit code $status). What the app said besides:" >&2
  grep -v '^HERMES_SMOKE_' "$work/app.log" | tail -20 >&2
  exit 1
fi
