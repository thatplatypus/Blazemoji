# check=skip=FromPlatformFlagConstDisallowed
# The Emojicode compiler bundled in this repo is the x86_64 Linux build, so every stage is linux/amd64.
# Build from the repository root:
#   docker compose up --build                                                        run the app (see docker-compose.yml)
#   docker build -f Blazemoji/Dockerfile -t blazemoji . && docker run -p 5000:8080 blazemoji    the same in one container
#   docker build -f Blazemoji/Dockerfile --target test --output type=cacheonly .     run the tests
#
# Three images come out of this file. "toolchain" is the service that compiles and runs
# programs, and "web" is the app people open in a browser; docker compose runs those two,
# with the toolchain shut off from the network. "all-in-one", the last stage and so the
# default, holds both, for running on your own machine from an IDE or with one command.

# The .NET SDK plus what the Emojicode compiler needs.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS sdk-deps
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh

# pmj, Packmoji's command, which builds the packages a project locks. It is the program
# Packmoji released, fetched and held to the digest in packmoji.pin: see docker/get-pmj.sh.
FROM sdk-deps AS pmj
COPY packmoji.pin /tmp/packmoji.pin
COPY docker/get-pmj.sh /tmp/get-pmj.sh
RUN /tmp/get-pmj.sh /tmp/packmoji.pin /pmj linux-x64

# What the tests need: the SDK, the compiler's needs, and pmj. scripts/test-in-docker.sh runs
# the tests in a container made from this stage alone. The variable is the one the toolchain
# service is configured by, so the service started there finds pmj as the tests do.
FROM sdk-deps AS sdk-toolchain
COPY --from=pmj /pmj /opt/pmj
ENV Packmoji__Path=/opt/pmj/pmj

FROM sdk-toolchain AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json ./
# The tests hold the pin's shape, and the image's pmj to the version it names.
COPY packmoji.pin ./
COPY Blazemoji/Blazemoji.csproj Blazemoji/
COPY Blazemoji.Core/Blazemoji.Core.csproj Blazemoji.Core/
COPY Blazemoji.Components/Blazemoji.Components.csproj Blazemoji.Components/
COPY Blazemoji.Toolchain/Blazemoji.Toolchain.csproj Blazemoji.Toolchain/
COPY Blazemoji.Toolchain.Local/Blazemoji.Toolchain.Local.csproj Blazemoji.Toolchain.Local/
COPY Blazemoji.Toolchain.Service/Blazemoji.Toolchain.Service.csproj Blazemoji.Toolchain.Service/
COPY Blazemoji.Test/Blazemoji.Test.csproj Blazemoji.Test/
# The test project references the app and the toolchain, so restoring and building it covers
# all three. The browser tests run on the host and are left out of the image.
RUN dotnet restore Blazemoji.Test/Blazemoji.Test.csproj
COPY Blazemoji/ Blazemoji/
COPY Blazemoji.Core/ Blazemoji.Core/
COPY Blazemoji.Components/ Blazemoji.Components/
COPY Blazemoji.Toolchain/ Blazemoji.Toolchain/
COPY Blazemoji.Toolchain.Local/ Blazemoji.Toolchain.Local/
COPY Blazemoji.Toolchain.Service/ Blazemoji.Toolchain.Service/
COPY Blazemoji.Test/ Blazemoji.Test/
RUN dotnet build Blazemoji.Test/Blazemoji.Test.csproj -c $BUILD_CONFIGURATION --no-restore

FROM build AS test
# The tests about hosting read the guide, and find the repository by its solution file.
COPY Blazemoji.sln ./
COPY docs/ docs/
# A skipped test here means the compiler tests did not run, and a hung test must fail the
# build instead of blocking it, so both are turned into failures. The tests that start
# processes run apart from the rest, and each run has an outer timeout, because under amd64
# emulation a process that compiles its own code while starting others can freeze for good
# (see scripts/test-in-docker.sh).
RUN timeout -k 10 900 dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -c $BUILD_CONFIGURATION --no-build --timeout 5m -- --fail-skips on --filter-namespace Blazemoji.Test.Toolchain
RUN timeout -k 10 900 dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -c $BUILD_CONFIGURATION --no-build --timeout 5m -- --fail-skips on --filter-not-namespace Blazemoji.Test.Toolchain

FROM build AS publish-web
RUN dotnet publish Blazemoji/Blazemoji.csproj -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false
# Without this file the page renders but never becomes interactive.
RUN test -f /app/publish/wwwroot/_framework/blazor.server.js

FROM build AS publish-toolchain
RUN dotnet publish Blazemoji.Toolchain.Service/Blazemoji.Toolchain.Service.csproj -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false
# The service is no use without an executable compiler and the package documentation.
RUN test -x /app/publish/emojicodec/emojicodec && test -f /app/publish/package-docs/s/documentation.json

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS toolchain
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh
WORKDIR /app
COPY --from=publish-toolchain /app/publish .
# Beside the service, which is where it looks for pmj unless it is told otherwise.
COPY --from=pmj /pmj ./pmj
# Programs are compiled and run under the temp directory, so the app user needs no write
# access to the application itself.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Blazemoji.Toolchain.Service.dll"]

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=publish-web /app/publish .
# Beside the web app, which fetches a project's packages itself: the toolchain it hands them
# to has no way to GitHub. What pmj fetches is kept under the temp directory, like everything
# else a container of this image writes.
COPY --from=pmj /pmj ./pmj
ENV Packmoji__Home=/tmp/packmoji
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Blazemoji.dll"]

# The whole app in one container, and what this file builds when no target is named, which
# is what an IDE does when asked to build and run a Dockerfile. The web app and the
# toolchain service run side by side and talk over loopback. Programs run here have the
# container's own access to the network, so this is for running on your own machine.
# docker compose builds the `toolchain` and `web` targets instead and keeps the toolchain
# on a network with no way out.
FROM toolchain AS all-in-one
WORKDIR /web
COPY --from=publish-web /app/publish .
COPY docker/all-in-one.sh /usr/local/bin/blazemoji-all-in-one
ENV ToolchainClient__BaseUrl=http://127.0.0.1:5290
# This image is what the public site runs. Both processes in it read this, and build
# packages of these scopes and no others.
ENV Packmoji__Scopes=thatplatypus,emojicode
# One pmj for both processes, and one folder for what it keeps. The service then finds every
# archive the web app fetched already there, and none has to be handed over.
ENV Packmoji__Path=/app/pmj/pmj
ENV Packmoji__Home=/tmp/packmoji
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/blazemoji-all-in-one"]
