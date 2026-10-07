#!/bin/sh
# Runs the unit and toolchain tests with the real Emojicode compiler in a throwaway
# linux/amd64 container. The sources are copied into the container and discarded with it, so repeated
# runs leave no images or build cache behind. NuGet packages are kept in the
# "blazemoji-nuget" volume between runs.
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
    dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -c Release --timeout 5m "$@" -- --fail-skips on
  ' sh "$@"
