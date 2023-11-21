#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER app
WORKDIR /app
EXPOSE 80
EXPOSE 443

USER root

RUN apt-get update 
RUN apt-get install -y g++ 
RUN apt-get install -y libncurses5

USER app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Blazemoji.Compiler/Blazemoji.Compiler.csproj", "Blazemoji.Compiler/"]
COPY ["Blazemoji.Compiler/packages/", "Blazemoji.Compiler/"]
COPY ["Blazemoji.Contracts/Blazemoji.Contracts.csproj", "Blazemoji.Contracts/"]
RUN dotnet restore "Blazemoji.Contracts/Blazemoji.Contracts.csproj"
RUN dotnet restore "Blazemoji.Compiler/Blazemoji.Compiler.csproj"

WORKDIR "/src/Blazemoji.Compiler/"
COPY /Blazemoji.Contracts/ .
COPY /Blazemoji.Compiler/ .

RUN dotnet build "Blazemoji.Compiler.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Blazemoji.Compiler.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Blazemoji.Compiler.dll"]