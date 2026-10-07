#!/bin/sh
# Builds the app image, starts it on a local port, runs the browser tests against it and
# removes the container afterwards, whether or not the tests pass.
#
#   scripts/e2e.sh                      run every browser test
#   scripts/e2e.sh --output Detailed    extra arguments go to dotnet test
#
# Screenshots land in Blazemoji.E2E/bin/Debug/net10.0/screenshots unless
# BLAZEMOJI_E2E_SCREENSHOTS names another folder.
set -eu
cd "$(dirname "$0")/.."

IMAGE=blazemoji
CONTAINER=blazemoji-e2e
PORT="${BLAZEMOJI_E2E_PORT:-5088}"

docker build -q -f Blazemoji/Dockerfile -t "$IMAGE" . > /dev/null

stop_app() {
  docker stop "$CONTAINER" > /dev/null 2>&1 || true
}
trap stop_app EXIT
stop_app

docker run -d --rm --name "$CONTAINER" --platform linux/amd64 -p "127.0.0.1:$PORT:8080" "$IMAGE" > /dev/null
until curl -s -o /dev/null "http://127.0.0.1:$PORT/"; do sleep 1; done

BLAZEMOJI_BASE_URL="http://127.0.0.1:$PORT" dotnet test --project Blazemoji.E2E "$@"
