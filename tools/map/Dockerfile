# Build context is the repository root, see build.docker.sh:
#   docker build -f tools/map/Dockerfile .

# Build the C# map generator d2-map.exe (win-x86, run under wine)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS mapgen

ARG GIT_VERSION
ARG GIT_HASH

WORKDIR /src
COPY ./global.json ./Directory.Build.props ./
COPY ./tools/map/src ./tools/map/src
RUN dotnet publish tools/map/src/D2Dotnet.Map.Generator/D2Dotnet.Map.Generator.csproj \
    -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true \
    -p:GitVersion="${GIT_VERSION:-unknown}" -p:GitHash="${GIT_HASH:-unknown}" -o /build/bin

# Build the .NET map server (self contained, so the wine image needs no .NET runtime)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server

WORKDIR /src
COPY ./global.json ./Directory.Build.props ./
COPY ./tools/map/src ./tools/map/src
RUN dotnet publish tools/map/src/D2Dotnet.Map/D2Dotnet.Map.csproj \
    -c Release -r linux-x64 --self-contained true -o /app/server

# Make a run environment with the map server and wine.
# .NET 10 needs a recent wine (tested with wine 9), so use Ubuntu 24.04's wine32 rather than tianon/wine:5.
FROM ubuntu:24.04

RUN dpkg --add-architecture i386 \
    && apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install --no-install-recommends -y wine wine32 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV WINEARCH=win32
ENV WINEDEBUG=-all

VOLUME [ "/app/game" ]

WORKDIR /app

COPY --from=server /app/server/ /app/

COPY --from=mapgen /build/bin/d2-map.exe /app/bin/d2-map.exe

ARG GIT_VERSION
ENV GIT_VERSION ${GIT_VERSION}

ARG GIT_HASH
ENV GIT_HASH ${GIT_HASH}

# Make sure InstallPath exists inside the registry
COPY ./tools/map/d2.install.reg /app/
EXPOSE 8899
CMD ["/app/D2Dotnet.Map"]
