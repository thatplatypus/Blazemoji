#!/bin/sh
# Entry point of the all-in-one image: the toolchain service on loopback, and the web app
# beside it. When either stops, the other is stopped as well and the container exits, so a
# crash is not hidden behind a page that half works.
set -u

cd /app
dotnet Blazemoji.Toolchain.Service.dll --urls http://127.0.0.1:5290 &
toolchain=$!

cd /web
dotnet Blazemoji.dll &
web=$!

asked_to_stop=0
stop_both() {
  kill -TERM "$toolchain" "$web" 2> /dev/null
}
trap 'asked_to_stop=1; stop_both' TERM INT

# Waiting on a short sleep, not sleeping outright, lets a signal be handled at once.
while kill -0 "$toolchain" 2> /dev/null && kill -0 "$web" 2> /dev/null; do
  sleep 1 &
  wait $!
done

stop_both
wait
[ "$asked_to_stop" = 1 ] || exit 1
