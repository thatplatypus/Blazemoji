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
#   scripts/desktop-smoke.sh --app <path>    test a copy that is already built: the program
#                                            itself, such as the one inside an unpacked release
#
# A window opens for a few seconds and closes itself. The run's projects go to a temporary
# folder, never to your own. The app reports in the words of Hermes's smoke protocol, so
# what judges a Hermes app's smoke run can judge this one: the last line that starts with
# HERMES_SMOKE_RESULT says PASSED or FAILED, and the exit code is 0 only for a pass.
# SMOKE_TIMEOUT (seconds, 120) is how long the page has to report. An app that has not
# closed a minute after that is stopped. SMOKE_OUTPUT names a folder to leave everything the
# app said, and its result, in. Needs perl, which macOS, most Linux and Git for Windows have.
# On Linux with no display, run it under xvfb-run.
set -eu
cd "$(dirname "$0")/.."

published=0
compile=0
app=""
while [ $# -gt 0 ]; do
  case "$1" in
    --published) published=1; shift ;;
    --compile) compile=1; shift ;;
    --app) app="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

work="$(mktemp -d)"
keep=0
trap '[ "$keep" -eq 1 ] || rm -rf "$work"' EXIT
limit="${SMOKE_TIMEOUT:-120}"

if [ -n "$app" ]; then
  case "$app" in
    /* | ?:*) ;;
    *) app="$(pwd)/$app" ;;
  esac
elif [ "$published" -eq 1 ]; then
  rid="$(dotnet --info | tr -d '\r' | awk '/^ *RID:/ { print $2; exit }')"
  echo "Publishing for $rid..."
  dotnet publish Blazemoji.Desktop/Blazemoji.Desktop.csproj -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$work/app" > "$work/build.log" 2>&1 \
    || { tail -30 "$work/build.log"; exit 1; }
  app="$work/app/Blazemoji"
else
  dotnet build Blazemoji.Desktop/Blazemoji.Desktop.csproj > "$work/build.log" 2>&1 \
    || { tail -30 "$work/build.log"; exit 1; }
  app="$(pwd)/Blazemoji.Desktop/bin/Debug/net10.0/Blazemoji"
fi

[ -f "$app" ] || app="$app.exe"

# Git for Windows gives its own kind of path, which a Windows program cannot open.
native() {
  if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi
}

# Started from somewhere else on purpose: the app must find its own files wherever it is run from.
# The app times itself out once it is up. The alarm is for one that hangs before that or
# while closing, and ends it with a signal, which counts as a failure below.
status=0
(
  cd "$work"
  HERMES_SMOKE_TEST=1 \
  HERMES_SMOKE_TEST_TIMEOUT="$limit" \
  HERMES_SMOKE_TEST_RESULT="$(native "$work/result.json")" \
  BLAZEMOJI_SMOKE_COMPILE="$compile" \
  BLAZEMOJI_SMOKE_PROJECTS="$(native "$work/projects")" \
    perl -e 'alarm shift; exec @ARGV or die "could not start $ARGV[0]: $!\n"' "$((limit + 60))" "$app" > "$work/app.log" 2>&1
) || status=$?

if [ -n "${SMOKE_OUTPUT:-}" ]; then
  mkdir -p "$SMOKE_OUTPUT"
  cp "$work/app.log" "$SMOKE_OUTPUT/" 2> /dev/null || true
  cp "$work/result.json" "$SMOKE_OUTPUT/" 2> /dev/null || true
fi

grep '^HERMES_SMOKE_' "$work/app.log" || true
if [ "$status" -ne 0 ] || ! grep -q '^HERMES_SMOKE_RESULT: PASSED' "$work/app.log"; then
  keep=1
  echo "The smoke run failed (exit code $status). What the app said besides:" >&2
  grep -v '^HERMES_SMOKE_' "$work/app.log" | tail -20 >&2
  echo "Everything it said, and its result, are kept in $work" >&2
  exit 1
fi
