#!/bin/sh
# Runs the unit and toolchain tests with the real Emojicode compiler in a throwaway
# linux/amd64 container, then starts the toolchain service there and runs the contract tests
# against it. The sources are copied into the container and discarded with it, so
# repeated runs add no images or build cache beyond the one "blazemoji-sdk-toolchain" image,
# which is rebuilt only when the toolchain stage of the Dockerfile changes. NuGet packages are
# kept in the "blazemoji-nuget" volume between runs.
#
# Under amd64 emulation on Apple Silicon a .NET process can stop for good: the emulator parks
# every thread and never wakes them. It happens when the process is compiling a lot of its own
# code while it also starts other processes, which is exactly what a test run does in its
# first seconds. The emulator has also been seen to abort the process outright. So the tests
# that start processes (everything in Blazemoji.Test.Toolchain) run on their own, the rest run
# afterwards, and a run that froze or was aborted is tried again, up to three times. A test
# that fails is never retried.
#
#   scripts/test-in-docker.sh                      run everything
#   scripts/test-in-docker.sh --output Detailed    extra arguments go to dotnet test
set -eu
cd "$(dirname "$0")/.."

IMAGE=blazemoji-sdk-toolchain
docker build -q -f Blazemoji/Dockerfile --target sdk-toolchain -t "$IMAGE" . > /dev/null

# The tests that need the Grapevine package run when it can be built from a local Grapevine
# checkout, and are left out otherwise.
scripts/build-grapevine.sh --if-needed || echo "Grapevine could not be built; the tests that need it are left out." >&2

docker run --rm --platform linux/amd64 \
  ${BLAZEMOJI_TEST_ENV:-} \
  -v "$PWD":/host:ro \
  -v blazemoji-nuget:/root/.nuget/packages \
  "$IMAGE" sh -c '
    set -e
    mkdir /src
    cd /host
    tar -cf - --exclude=./.git --exclude=bin --exclude=obj --exclude=./.superpowers --exclude=./.cerberus . | tar -xf - -C /src
    cd /src
    extra="$*"

    # run_tests <project> <arguments for the test host...>
    # Exit codes 124 and 137 are the outer timeout ending a frozen process, and 133 is the
    # emulator aborting it. Those are tried again; anything else is the answer.
    run_tests() {
      project="$1"
      shift
      attempt=1
      while :; do
        status=0
        timeout -k 10 300 dotnet test --project "$project" -c Release --timeout 4m $extra -- --fail-skips on "$@" || status=$?
        case "$status" in
          124|133|137)
            if [ "$attempt" -ge 3 ]; then
              return "$status"
            fi
            attempt=$((attempt + 1))
            echo "The test process froze or was aborted by the emulator (exit $status). Trying again." >&2
            ;;
          *)
            return "$status"
            ;;
        esac
      done
    }

    if [ -f Blazemoji.Toolchain.Local/packages/grapevine/libgrapevine.a ]; then
      without=""
    else
      without="--filter-not-trait Requires=Grapevine"
    fi

    run_tests Blazemoji.Test/Blazemoji.Test.csproj --filter-namespace Blazemoji.Test.Toolchain $without
    run_tests Blazemoji.Test/Blazemoji.Test.csproj --filter-not-namespace Blazemoji.Test.Toolchain $without

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

    TOOLCHAIN_BASE_URL=http://127.0.0.1:5290 run_tests Blazemoji.Toolchain.ContractTests/Blazemoji.Toolchain.ContractTests.csproj
  ' sh "$@"
