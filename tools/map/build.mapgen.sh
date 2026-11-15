#!/bin/bash
mkdir bin -p

# Generator sources: map/ inside the Docker build stage, native/ when run from tools/map
SRC=${MAP_SRC:-$([ -d map ] && echo map || echo native)}

GIT_VERSION=${GIT_VERSION:-$(git describe --abbrev=4 --dirty --always --tags)}
GIT_HASH=${GIT_HASH:-$(git rev-parse HEAD)}

echo $GIT_VERSION
echo $GIT_HASH

i686-w64-mingw32-g++ -o bin/d2-map.exe \
    -Wno-write-strings \
    -static-libgcc -static-libstdc++ \
    -DGIT_VERSION=\"${GIT_VERSION}\" \
    -DGIT_HASH=\"${GIT_HASH}\" \
    $SRC/json.c $SRC/log.c $SRC/map.c $SRC/offset.c $SRC/d2_client.c $SRC/main.c 
echo $(date --iso-8601=seconds) "Build done" $GIT_VERSION $GIT_HASH