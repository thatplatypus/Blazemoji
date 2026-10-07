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

# A rebuild after a source change leaves the previous image untagged. It is removed here so
# that repeated runs do not pile up half-gigabyte images.
previous="$(docker image inspect --format '{{.Id}}' "$IMAGE" 2> /dev/null || true)"
docker build -q -f Blazemoji/Dockerfile -t "$IMAGE" . > /dev/null
current="$(docker image inspect --format '{{.Id}}' "$IMAGE")"
if [ -n "$previous" ] && [ "$previous" != "$current" ]; then
  docker image rm "$previous" > /dev/null 2>&1 || true
fi

stop_app() {
  docker stop "$CONTAINER" > /dev/null 2>&1 || true
}
trap stop_app EXIT
stop_app

docker run -d --rm --name "$CONTAINER" --platform linux/amd64 -p "127.0.0.1:$PORT:8080" "$IMAGE" > /dev/null
waited=0
until curl -s -o /dev/null "http://127.0.0.1:$PORT/"; do
  waited=$((waited + 1))
  if [ "$waited" -gt 120 ]; then
    echo "The app did not answer on port $PORT within two minutes." >&2
    exit 1
  fi
  sleep 1
done

BLAZEMOJI_BASE_URL="http://127.0.0.1:$PORT" dotnet test --project Blazemoji.E2E "$@"
