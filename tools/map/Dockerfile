# Build context is the repository root, see build.docker.sh:
#   docker build -f tools/map/Dockerfile .

# Build mapgen in a container
FROM ubuntu:20.04 as build

RUN apt-get update \
    && apt-get install --no-install-recommends --no-install-suggests git -y \
    mingw-w64

RUN mkdir -p /build/map
COPY ./tools/map/native /build/map
COPY ./tools/map/build.mapgen.sh /build

WORKDIR /build
ARG GIT_VERSION
ENV GIT_VERSION ${GIT_VERSION}

ARG GIT_HASH
ENV GIT_HASH ${GIT_HASH}

RUN bash build.mapgen.sh

# Build the .NET map server (self contained, so the wine image needs no .NET runtime)
FROM mcr.microsoft.com/dotnet/sdk:10.0 as server

WORKDIR /src
COPY ./global.json ./Directory.Build.props ./
COPY ./tools/map/src ./tools/map/src
RUN dotnet publish tools/map/src/D2Dotnet.Map/D2Dotnet.Map.csproj \
    -c Release -r linux-x64 --self-contained true -o /app/server

# Make a run environment with the map server and wine
FROM tianon/wine:5

ENV WINEARCH=win32
ENV WINEDEBUG=-all

VOLUME [ "/app/game" ]

WORKDIR /app

COPY --from=server /app/server/ /app/

COPY --from=build /build/bin/d2-map.exe /app/bin/d2-map.exe

ARG GIT_VERSION
ENV GIT_VERSION ${GIT_VERSION}

ARG GIT_HASH
ENV GIT_HASH ${GIT_HASH}

# Make sure InstallPath exists inside the registry
COPY ./tools/map/d2.install.reg /app/
EXPOSE 8899
CMD ["/app/D2Dotnet.Map"]
