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
