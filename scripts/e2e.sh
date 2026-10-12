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

# The images include the Grapevine package and its sample project when they have been built
# from a local Grapevine checkout. Without them the Grapevine browser test skips itself.
scripts/build-grapevine.sh --if-needed || echo "Grapevine could not be built; its browser test will be skipped." >&2

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

# The toolchain service must not be able to reach the outside world. First show that the
# check can fail at all: the same tool, in the same container, has to be able to reach
# something. Otherwise a container that is not running would pass as having no route out.
waited=0
until docker compose -p "$PROJECT" exec -T toolchain curl -fs -m 5 -o /dev/null http://127.0.0.1:8080/health; do
  waited=$((waited + 1))
  if [ "$waited" -gt 60 ]; then
    echo "The toolchain container did not answer its own health check, so its isolation cannot be checked." >&2
    docker compose -p "$PROJECT" logs toolchain | tail -40 >&2
    exit 1
  fi
  sleep 1
done
if docker compose -p "$PROJECT" exec -T toolchain curl -s -m 5 -o /dev/null https://example.com; then
  echo "The toolchain container reached the internet. It must be on an internal network only." >&2
  exit 1
fi
echo "toolchain container answers locally and has no route out: ok"

# The website's toolchain builds packages of two scopes only. A compose file whose list had
# grown, or had become a star, would build more.
scopes="$(docker compose -p "$PROJECT" exec -T toolchain printenv Packmoji__Scopes || true)"
if [ "$scopes" != "thatplatypus,emojicode" ]; then
  echo "The toolchain container builds packages of the scopes '$scopes'. It must be limited to thatplatypus,emojicode." >&2
  exit 1
fi
echo "toolchain container builds packages of thatplatypus and emojicode only: ok"

# The web app fetches a project's packages itself, so its image has to hold a pmj that starts
# as the image's own user.
if ! docker compose -p "$PROJECT" exec -T web /app/pmj/pmj --version > /dev/null 2>&1; then
  echo "The web container has no pmj that answers at /app/pmj/pmj." >&2
  docker compose -p "$PROJECT" logs web | tail -20 >&2
  exit 1
fi
echo "web container has a pmj that answers: ok"

# The tests with the real compiler and the real pmj run in the SDK image, as root, on a writable
# disk and with a network. This is the only place the toolchain image runs as it is deployed:
# read-only root, the app's own user, a small tmpfs at /tmp and no route out. So it is the only
# place that shows the image can build a set at all, from archives it was handed, with pmj's
# cache and built packages in the folders it is allowed to write.
json_text() {
  sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' "$1" | awk '{ printf "%s\\n", $0 }'
}

packages_dir=Blazemoji.Toolchain.ContractTests/Packages
for archive in \
  "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8 hello_words %40thatplatypus%2Fhello_words" \
  "9da45105dcb3746b7e01d8bd0e702ef3100c90be1be7aa650ea0a9367b8fae3a hello_greeter %40thatplatypus%2Fhello_greeter"; do
  read -r digest file query <<EOF
$archive
EOF
  status="$(docker compose -p "$PROJECT" exec -T toolchain curl -s -m 60 -o /dev/null -w '%{http_code}' \
    -X PUT -H 'Content-Type: application/octet-stream' --data-binary @- \
    "http://127.0.0.1:8080/dependencies/archives/$digest?name=$query&version=0.1.0" < "$packages_dir/$file-0.1.0.pmj.tar.gz" || true)"
  if [ "$status" != "204" ]; then
    echo "The toolchain container answered $status when given the archive of $file. It must answer 204." >&2
    docker compose -p "$PROJECT" logs toolchain | tail -40 >&2
    exit 1
  fi
done

answer="$(printf '{"manifest":"%s","lockfile":"%s"}' "$(json_text "$packages_dir/packmoji.json")" "$(json_text "$packages_dir/packmoji.lock")" \
  | docker compose -p "$PROJECT" exec -T toolchain curl -s -m 300 -X POST -H 'Content-Type: application/json' --data-binary @- \
    http://127.0.0.1:8080/dependencies || true)"
case "$answer" in
  *'"state":"ready"'*) ;;
  *)
    echo "The toolchain container did not build a set of packages. It answered: $answer" >&2
    docker compose -p "$PROJECT" logs toolchain | tail -40 >&2
    exit 1
    ;;
esac
echo "toolchain container builds a set of packages as it is deployed, with no network: ok"

# Docker's resolver answers on an internal network too. A name that resolves is not a route,
# but it is worth knowing, because a lookup can carry data out.
if docker compose -p "$PROJECT" exec -T toolchain getent hosts example.com > /dev/null 2>&1; then
  echo "note: the toolchain container can resolve public names, though it cannot connect to them"
else
  echo "toolchain container cannot resolve public names either"
fi

BLAZEMOJI_BASE_URL="http://127.0.0.1:$BLAZEMOJI_PORT" dotnet test --project Blazemoji.E2E "$@"
