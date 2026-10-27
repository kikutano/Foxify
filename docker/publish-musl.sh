#!/bin/sh
# Native AOT publish for Alpine (musl), inside the .NET SDK's Alpine image: a glibc host cannot link for musl.
#
#   docker/publish-musl.sh linux-musl-x64 publish-musl [version]
#
# Run on a host of the same architecture as the RID: Native AOT does not cross-compile between architectures.
# The sources are mounted read-only and copied, so the container's restore for musl never touches the host's
# obj/ folders, and only the output folder is written (owned by root).
set -eu

rid="$1"
out="$2"
version="${3:-0.0.0-dev}"

mkdir -p "$out"
docker run --rm \
  -v "$PWD:/src:ro" \
  -v "$(cd "$out" && pwd):/out" \
  -e RID="$rid" -e VERSION="$version" \
  mcr.microsoft.com/dotnet/sdk:10.0-alpine \
  sh -euc '
    apk add --no-cache clang build-base zlib-dev >/dev/null
    cp -r /src /build
    rm -rf /build/src/*/bin /build/src/*/obj
    dotnet publish /build/src/Tinyprobe.Cli/Tinyprobe.Cli.csproj -c Release -r "$RID" -o /build/publish -p:Version="$VERSION"
    cp /build/publish/tinyprobe /out/tinyprobe
    /out/tinyprobe --version
  '
