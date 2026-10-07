#!/bin/sh
# Starts the toolchain service in a container with its port on this machine only, so that
# the web app can be run and debugged from an IDE, or with `dotnet run --project Blazemoji`.
# The app looks for the service at http://localhost:5290 unless told otherwise, and the
# service cannot run outside x86_64 Linux, so on a Mac this is what gives Run Code something
# to talk to.
#
#   scripts/dev-toolchain.sh          build the image if need be, and start the service
#   scripts/dev-toolchain.sh stop     stop it
#
# Unlike `docker compose up`, programs run through this service can reach the network.
# TOOLCHAIN_PORT picks another port; then set ToolchainClient__BaseUrl to match.
set -eu
cd "$(dirname "$0")/.."

NAME=blazemoji-dev-toolchain
PORT="${TOOLCHAIN_PORT:-5290}"

docker rm -f "$NAME" > /dev/null 2>&1 || true
if [ "${1:-}" = stop ]; then
  echo "stopped"
  exit 0
fi

scripts/build-grapevine.sh --if-needed || echo "Grapevine could not be built; projects that import it will not compile." >&2
docker build --quiet --file Blazemoji/Dockerfile --target toolchain --tag blazemoji-toolchain . > /dev/null

# The same limits the compose file gives the service.
docker run --detach --name "$NAME" --platform linux/amd64 \
  --publish "127.0.0.1:$PORT:8080" \
  --read-only --tmpfs /tmp:exec,size=768m,mode=1777 \
  --cap-drop ALL --security-opt no-new-privileges \
  --pids-limit 1024 --memory 2g --cpus 4 \
  blazemoji-toolchain > /dev/null

waited=0
until curl -fs -o /dev/null "http://127.0.0.1:$PORT/health"; do
  waited=$((waited + 1))
  if [ "$waited" -gt 60 ]; then
    echo "The toolchain service did not answer on port $PORT within a minute." >&2
    docker logs "$NAME" 2>&1 | tail -20 >&2
    exit 1
  fi
  sleep 1
done
echo "toolchain service is at http://localhost:$PORT"
