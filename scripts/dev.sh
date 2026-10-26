#!/bin/sh
# Development tasks for tinyprobe, the same steps CI runs, on your machine.
#
#   scripts/dev.sh <command>
#
# Commands:
#   build     Build the solution (Debug).
#   test      Run the unit tests.
#   server    Start the test server on http://localhost:5122, in the foreground.
#   e2e       Run every workflow of docs/example and the JSON protocol checks with the
#             development binary, against the test server (started and stopped if needed).
#   example   Run the workflows of example/ against JSONPlaceholder (needs internet).
#   check     build + test + e2e: run it before opening a pull request.
#   install   Publish a development binary to .dev/tinyprobe and link it as tinyprobe-dev
#             in $HOME/.local/bin. The VS Code extension can use it with tinyprobe.path.
#   aot       Publish the Native AOT binary to publish/, like a release (needs clang, zlib).
#
# Environment variables:
#   TINYPROBE_PORT   port of the test server (default: 5122)
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
PORT="${TINYPROBE_PORT:-5122}"
BASE_URL="http://localhost:$PORT"
DEV_DIR="$ROOT/.dev"
DEV_BIN="$DEV_DIR/tinyprobe/tinyprobe"
SERVER_PROJECT="$ROOT/tests/Tinyprobe.RealServer.Tests"

fail() { echo "dev: $*" >&2; exit 1; }
step() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

rid() {
  case "$(uname -s)" in
    Linux) os="linux" ;;
    Darwin) os="osx" ;;
    *) fail "unsupported OS '$(uname -s)'." ;;
  esac
  case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
    *) fail "unsupported architecture '$(uname -m)'." ;;
  esac
  echo "$os-$arch"
}

build() {
  step "Build"
  dotnet build "$ROOT/Tinyprobe.slnx" -nologo -v quiet
}

unit_tests() {
  step "Unit tests"
  dotnet test "$ROOT/Tinyprobe.slnx" --no-build -nologo -v quiet
}

# A framework-dependent build of the CLI: fast, no native toolchain needed, version 0.0.0-dev.
publish_dev() {
  step "Development binary"
  dotnet publish "$ROOT/src/Tinyprobe.Cli/Tinyprobe.Cli.csproj" -c Release -p:PublishAot=false \
    -o "$DEV_DIR/tinyprobe" -nologo -v quiet
  echo "$DEV_BIN"
}

server_up() { curl -sf -o /dev/null "$BASE_URL/api/public"; }

SERVER_PID=""
start_server() {
  if server_up; then
    echo "Test server already running on $BASE_URL"
    return
  fi
  step "Start test server on $BASE_URL"
  mkdir -p "$DEV_DIR"
  dotnet run --project "$SERVER_PROJECT" --urls "$BASE_URL" > "$DEV_DIR/server.log" 2>&1 &
  SERVER_PID=$!
  trap stop_server EXIT INT TERM
  i=0
  until server_up; do
    i=$((i + 1))
    [ "$i" -le 120 ] || { cat "$DEV_DIR/server.log"; fail "the test server did not start."; }
    sleep 1
  done
}

stop_server() {
  [ -n "$SERVER_PID" ] || return 0
  # dotnet run starts the server as a child process: stop both.
  pkill -P "$SERVER_PID" 2>/dev/null || true
  kill "$SERVER_PID" 2>/dev/null || true
  SERVER_PID=""
}

e2e() {
  command -v jq > /dev/null || fail "jq is needed for the protocol checks."
  publish_dev
  start_server
  cd "$ROOT"

  step "End-to-end (docs/example/workflows)"
  "$DEV_BIN" run docs/example/workflows -e docs/environment.yaml --var "baseUrl=$BASE_URL/"

  step "End-to-end (inspect, call, ndjson)"
  "$DEV_BIN" inspect docs/example/api/testserver.yaml
  "$DEV_BIN" inspect docs/example/workflows/users.yaml --json \
    | jq -e '.valid and .kind == "workflow" and (.steps | length) > 0' > /dev/null
  "$DEV_BIN" call docs/example/api/testserver.yaml status --with code=404 \
    -e docs/environment.yaml --var "baseUrl=$BASE_URL/"
  "$DEV_BIN" run docs/example/workflows/users.yaml -e docs/environment.yaml \
    --var "baseUrl=$BASE_URL/" --reporter ndjson > "$DEV_DIR/events.ndjson"
  jq -e -s '.[0].event == "run_started" and .[-1].event == "run_finished" and .[-1].success' \
    "$DEV_DIR/events.ndjson" > /dev/null
  "$DEV_BIN" run docs/example -e docs/environment.yaml --var "baseUrl=$BASE_URL/" --reporter ndjson \
    > "$DEV_DIR/suite.ndjson"
  jq -e -s '.[0].event == "suite_started" and (.[0].files | length) == 4
    and .[-1].event == "suite_finished" and .[-1].success' "$DEV_DIR/suite.ndjson" > /dev/null
  echo "Protocol checks passed."
}

example() {
  publish_dev
  cd "$ROOT/example"
  step "Example (JSONPlaceholder)"
  for workflow in workflows/*.yaml; do
    "$DEV_BIN" run "$workflow" -e environment.yaml
  done
}

install_dev() {
  publish_dev
  mkdir -p "$HOME/.local/bin"
  ln -sf "$DEV_BIN" "$HOME/.local/bin/tinyprobe-dev"
  echo "Linked $HOME/.local/bin/tinyprobe-dev -> $DEV_BIN"
}

aot() {
  command -v clang > /dev/null || fail "Native AOT needs clang and zlib (Debian/Ubuntu: sudo apt-get install clang zlib1g-dev)."
  step "Native AOT ($(rid))"
  dotnet publish "$ROOT/src/Tinyprobe.Cli/Tinyprobe.Cli.csproj" -c Release -r "$(rid)" -o "$ROOT/publish" -nologo
  ls -lh "$ROOT/publish/tinyprobe"
}

case "${1:-}" in
  build) build ;;
  test) build && unit_tests ;;
  server) cd "$ROOT" && exec dotnet run --project "$SERVER_PROJECT" --urls "$BASE_URL" ;;
  e2e) e2e ;;
  example) example ;;
  check) build && unit_tests && e2e && step "All checks passed" ;;
  install) install_dev ;;
  aot) aot ;;
  ""|-h|--help|help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) fail "unknown command '$1'. See scripts/dev.sh --help." ;;
esac
