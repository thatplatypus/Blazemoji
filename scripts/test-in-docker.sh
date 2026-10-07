#!/bin/sh
# Runs the unit and toolchain tests with the real Emojicode compiler in a throwaway
# linux/amd64 container, then starts the toolchain service there and runs the contract tests
# against it. The sources are copied into the container and discarded with it, so
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

    # The contract tests are black-box: they are pointed at a running toolchain service and
    # know nothing else about it. The service was built with the tests above.
    dotnet Blazemoji.Toolchain.Service/bin/Release/net10.0/Blazemoji.Toolchain.Service.dll --urls http://127.0.0.1:5290 > /tmp/toolchain-service.log 2>&1 &
    waited=0
    until curl -fs http://127.0.0.1:5290/health > /dev/null; do
      waited=$((waited + 1))
      if [ "$waited" -gt 60 ]; then
        echo "The toolchain service did not start:" >&2
        cat /tmp/toolchain-service.log >&2
        exit 1
      fi
      sleep 1
    done

    TOOLCHAIN_BASE_URL=http://127.0.0.1:5290 timeout -k 10 600 dotnet test --project Blazemoji.Toolchain.ContractTests/Blazemoji.Toolchain.ContractTests.csproj -c Release --timeout 5m "$@" -- --fail-skips on
  ' sh "$@"
