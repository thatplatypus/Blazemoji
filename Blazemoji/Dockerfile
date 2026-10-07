# check=skip=FromPlatformFlagConstDisallowed
# The Emojicode compiler bundled in this repo is the x86_64 Linux build, so every stage is linux/amd64.
# Build from the repository root:
#   docker build -f Blazemoji/Dockerfile --target test --output type=cacheonly .     run the tests
#   docker build -f Blazemoji/Dockerfile -t blazemoji .                              build the app image

# The .NET SDK plus what the Emojicode compiler needs. scripts/test-in-docker.sh runs the
# tests in a container made from this stage alone.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS sdk-toolchain
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh

FROM sdk-toolchain AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json ./
COPY Blazemoji/Blazemoji.csproj Blazemoji/
COPY Blazemoji.Toolchain/Blazemoji.Toolchain.csproj Blazemoji.Toolchain/
COPY Blazemoji.Test/Blazemoji.Test.csproj Blazemoji.Test/
# The test project references the app and the toolchain, so restoring and building it covers
# all three. The browser tests run on the host and are left out of the image.
RUN dotnet restore Blazemoji.Test/Blazemoji.Test.csproj
COPY Blazemoji/ Blazemoji/
COPY Blazemoji.Toolchain/ Blazemoji.Toolchain/
COPY Blazemoji.Test/ Blazemoji.Test/
RUN dotnet build Blazemoji.Test/Blazemoji.Test.csproj -c $BUILD_CONFIGURATION --no-restore

FROM build AS test
# A skipped test here means the compiler tests did not run, and a hung test must fail the
# build instead of blocking it, so both are turned into failures. The outer timeout is for
# the rare case, seen under amd64 emulation, where the whole test process freezes.
RUN timeout -k 10 900 dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -c $BUILD_CONFIGURATION --no-build --timeout 5m -- --fail-skips on

FROM build AS publish
RUN dotnet publish Blazemoji/Blazemoji.csproj -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false
# Without this file the page renders but never becomes interactive.
RUN test -f /app/publish/wwwroot/_framework/blazor.server.js

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS final
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh
WORKDIR /app
COPY --from=publish /app/publish .
# Programs are compiled and run under the temp directory, so the app user needs no write
# access to the application itself.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Blazemoji.dll"]
