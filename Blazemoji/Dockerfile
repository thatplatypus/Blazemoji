# check=skip=FromPlatformFlagConstDisallowed
# Emojicode ships x86_64 Linux binaries only, so every stage is linux/amd64.
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
COPY Blazemoji.Test/Blazemoji.Test.csproj Blazemoji.Test/
RUN dotnet restore Blazemoji.sln
COPY Blazemoji/ Blazemoji/
COPY Blazemoji.Test/ Blazemoji.Test/
RUN dotnet build Blazemoji.sln -c $BUILD_CONFIGURATION --no-restore

FROM build AS test
RUN dotnet test --solution Blazemoji.sln -c $BUILD_CONFIGURATION --no-build

FROM build AS publish
RUN dotnet publish Blazemoji/Blazemoji.csproj -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false
# Without this file the page renders but never becomes interactive.
RUN test -f /app/publish/wwwroot/_framework/blazor.server.js

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS final
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh
# The runner still writes its compiled binary into the working directory, so the
# app user has to own /app. Phase 1 moves that output to a per-run temp directory.
USER $APP_UID
WORKDIR /app
COPY --from=publish --chown=$APP_UID /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Blazemoji.dll"]
