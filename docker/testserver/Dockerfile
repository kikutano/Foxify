# The test server of docs/example (tests/Tinyprobe.RealServer.Tests), so the examples run without the .NET SDK:
#
#   docker run --rm -p 127.0.0.1:5122:5122 ghcr.io/kikutano/tinyprobe-testserver
#
# It accepts fixed credentials and fake tokens on purpose: publish it on 127.0.0.1 only, never on a
# network others can reach. Its data is in memory, and a new container starts from the seed data.
#
# Built from the repository root (docker build -f docker/testserver/Dockerfile .). The server is plain .NET,
# not Native AOT: it is published once on the build machine and the same files run on amd64 and arm64,
# so a multi-platform build needs no emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY tests/Tinyprobe.RealServer.Tests/ ./
RUN dotnet publish -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=5122
EXPOSE 5122
# The non-root user of the .NET images.
USER app
ENTRYPOINT ["dotnet", "Tinyprobe.RealServer.Tests.dll"]
