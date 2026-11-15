#!/bin/bash
# Publish the C# map generator (src/D2Dotnet.Map.Generator) as bin/d2-map.exe, a win-x86 single file exe run under wine.
set -e
cd "$(dirname "$0")"
mkdir -p bin

GIT_VERSION=${GIT_VERSION:-$(git describe --abbrev=4 --dirty --always --tags)}
GIT_HASH=${GIT_HASH:-$(git rev-parse HEAD)}

echo $GIT_VERSION
echo $GIT_HASH

dotnet publish src/D2Dotnet.Map.Generator/D2Dotnet.Map.Generator.csproj \
    -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true \
    -p:GitVersion="$GIT_VERSION" -p:GitHash="$GIT_HASH" -o bin
echo $(date --iso-8601=seconds) "Build done" $GIT_VERSION $GIT_HASH
