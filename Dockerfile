#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Blazemoji.Compiler/Blazemoji.Compiler.csproj", "Blazemoji.Compiler/"]
COPY ["Blazemoji.Contracts/Blazemoji.Contracts.csproj", "Blazemoji.Contracts/"]
RUN dotnet restore "./Blazemoji.Compiler/./Blazemoji.Compiler.csproj"
WORKDIR "/src/Blazemoji.Compiler"
COPY . .
RUN dotnet build "./Blazemoji.Compiler.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Blazemoji.Compiler.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Blazemoji.Compiler.dll"]