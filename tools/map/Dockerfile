# Build context is the repository root, see build.docker.sh:
#   docker build -f tools/map/Dockerfile .
#
# The map server generates maps in-process with the managed DRLG port: no wine, no game DLLs, 64-bit only.
# Mount a Diablo II: Resurrected install (or a folder of extracted game files) at /app/game.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY ./global.json ./Directory.Build.props ./
COPY ./tools/map/src ./tools/map/src
RUN dotnet publish tools/map/src/D2Dotnet.Map/D2Dotnet.Map.csproj \
    -c Release -r linux-x64 --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=build /app/ /app/

ARG GIT_VERSION
ENV GIT_VERSION=${GIT_VERSION}

ARG GIT_HASH
ENV GIT_HASH=${GIT_HASH}

VOLUME [ "/app/game" ]
EXPOSE 8899
ENTRYPOINT ["dotnet", "/app/D2Dotnet.Map.dll"]
