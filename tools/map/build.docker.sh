#!/bin/bash
GIT_VERSION=${GIT_VERSION:-$(git describe --abbrev=4 --dirty --always --tags)}
GIT_HASH=${GIT_HASH:-$(git rev-parse HEAD)}

# The image is built from the repository root so the shared build settings are available
ROOT=$(cd "$(dirname "$0")/../.." && pwd)

docker build --build-arg GIT_HASH=$GIT_HASH --build-arg GIT_VERSION=$GIT_VERSION -t d2dotnet/map -f "$ROOT/tools/map/Dockerfile" "$ROOT"
