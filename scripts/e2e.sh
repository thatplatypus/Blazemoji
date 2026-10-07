#!/bin/sh
# Builds the two images, starts them with docker compose, checks that the toolchain service
# has no route out, runs the browser tests against the web app, and takes everything down
# again whether or not the tests pass.
#
#   scripts/e2e.sh                      run every browser test
#   scripts/e2e.sh --output Detailed    extra arguments go to dotnet test
#
# Screenshots land in Blazemoji.E2E/bin/Debug/net10.0/screenshots unless
# BLAZEMOJI_E2E_SCREENSHOTS names another folder.
set -eu
cd "$(dirname "$0")/.."

PROJECT=blazemoji-e2e
export BLAZEMOJI_PORT="${BLAZEMOJI_E2E_PORT:-5088}"

image_id() {
  docker image inspect --format '{{.Id}}' "$1" 2> /dev/null || true
}

take_down() {
  docker compose -p "$PROJECT" down > /dev/null 2>&1 || true
}
trap take_down EXIT
take_down

# A rebuild after a source change leaves the previous image untagged. Those are removed here
# so that repeated runs do not pile up images.
previous_web="$(image_id blazemoji-web)"
previous_toolchain="$(image_id blazemoji-toolchain)"
docker compose -p "$PROJECT" build --quiet
for pair in "$previous_web:blazemoji-web" "$previous_toolchain:blazemoji-toolchain"; do
  previous="${pair%:*}"
  if [ -n "$previous" ] && [ "$previous" != "$(image_id "${pair##*:}")" ]; then
    docker image rm "$previous" > /dev/null 2>&1 || true
  fi
done

docker compose -p "$PROJECT" up -d > /dev/null

waited=0
until curl -s -o /dev/null "http://127.0.0.1:$BLAZEMOJI_PORT/"; do
  waited=$((waited + 1))
  if [ "$waited" -gt 120 ]; then
    echo "The app did not answer on port $BLAZEMOJI_PORT within two minutes." >&2
    exit 1
  fi
  sleep 1
done

# The toolchain service must not be able to reach the outside world.
if docker compose -p "$PROJECT" exec -T toolchain curl -s -m 5 -o /dev/null https://example.com; then
  echo "The toolchain container reached the internet. It must be on an internal network only." >&2
  exit 1
fi
echo "toolchain container has no route out: ok"

BLAZEMOJI_BASE_URL="http://127.0.0.1:$BLAZEMOJI_PORT" dotnet test --project Blazemoji.E2E "$@"
