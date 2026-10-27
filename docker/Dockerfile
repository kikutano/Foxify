# The tinyprobe image: the release's linux-musl binary on Alpine.
#
#   docker run --rm -v "$PWD:/work" ghcr.io/kikutano/tinyprobe:<version> run tinyprobe/workflows
#
# Built by the Release workflow from the binaries of the release itself, so the image runs the same file
# as the tarball (same checksum). The build context holds linux/amd64/tinyprobe and linux/arm64/tinyprobe;
# nothing is compiled here, so a multi-platform build needs no emulation.
#
# Alpine, not a distroless base: CI servers that run jobs inside an image (GitLab CI, Bitbucket, CircleCI)
# start a shell in it. runtime-deps carries what the binary loads: musl, libgcc, OpenSSL and the CA certificates.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine

ARG TARGETPLATFORM
COPY --chmod=755 ${TARGETPLATFORM}/tinyprobe /usr/local/bin/tinyprobe

# Root, as most CLI images: the workflows are a mounted folder or a CI checkout owned by another user, and the
# JUnit report is written into it. Locally, `--user "$(id -u):$(id -g)"` makes the report yours.
WORKDIR /work
ENTRYPOINT ["tinyprobe"]
