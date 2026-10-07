#!/bin/sh
# Runs the unit and toolchain tests with the real Emojicode compiler in a throwaway
# linux/amd64 container. The sources are copied into the container and discarded with it, so
# repeated runs add no images or build cache beyond the one "blazemoji-sdk-toolchain" image,
# which is rebuilt only when the toolchain stage of the Dockerfile changes. NuGet packages are
# kept in the "blazemoji-nuget" volume between runs.
#
# Under amd64 emulation the .NET test process has occasionally frozen outright (every thread
# parked, no test started). The run is therefore given 15 minutes and killed if it overruns;
# run it again if that happens.
#
#   scripts/test-in-docker.sh                      run everything
#   scripts/test-in-docker.sh --output Detailed    extra arguments go to dotnet test
set -eu
cd "$(dirname "$0")/.."

IMAGE=blazemoji-sdk-toolchain
docker build -q -f Blazemoji/Dockerfile --target sdk-toolchain -t "$IMAGE" . > /dev/null

docker run --rm --platform linux/amd64 \
  -v "$PWD":/host:ro \
  -v blazemoji-nuget:/root/.nuget/packages \
  "$IMAGE" sh -c '
    mkdir /src
    cd /host
    tar -cf - --exclude=./.git --exclude=bin --exclude=obj --exclude=./.superpowers --exclude=./.cerberus . | tar -xf - -C /src
    cd /src
    timeout -k 10 900 dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -c Release --timeout 5m "$@" -- --fail-skips on
  ' sh "$@"
