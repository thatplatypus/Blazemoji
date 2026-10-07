# check=skip=FromPlatformFlagConstDisallowed
# The Emojicode compiler bundled in this repo is the x86_64 Linux build, so every stage is linux/amd64.
# Build from the repository root:
#   docker build -f Blazemoji/Dockerfile --target test .     run the tests
#   docker build -f Blazemoji/Dockerfile -t blazemoji .      build the app image

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh
WORKDIR /src
COPY global.json Blazemoji.sln ./
COPY Blazemoji/Blazemoji.csproj Blazemoji/
COPY Blazemoji.Toolchain/Blazemoji.Toolchain.csproj Blazemoji.Toolchain/
COPY Blazemoji.Test/Blazemoji.Test.csproj Blazemoji.Test/
RUN dotnet restore Blazemoji.sln
COPY Blazemoji/ Blazemoji/
COPY Blazemoji.Toolchain/ Blazemoji.Toolchain/
COPY Blazemoji.Test/ Blazemoji.Test/
RUN dotnet build Blazemoji.sln -c $BUILD_CONFIGURATION --no-restore

FROM build AS test
# A skipped test here means the compiler tests did not run, and a hung test must fail the
# build instead of blocking it, so both are turned into failures.
RUN dotnet test --solution Blazemoji.sln -c $BUILD_CONFIGURATION --no-build --timeout 5m -- --fail-skips on

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
