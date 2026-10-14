#!/bin/sh
# Foxify installer for Linux and macOS.
#
#   curl -fsSL https://raw.githubusercontent.com/kikutano/foxify/main/install.sh | sh
#
# Environment variables:
#   FOXIFY_VERSION      version to install, e.g. 0.1.0 (default: latest release)
#   FOXIFY_INSTALL_DIR  target directory (default: $HOME/.local/bin)
set -eu

REPO="kikutano/foxify"
INSTALL_DIR="${FOXIFY_INSTALL_DIR:-$HOME/.local/bin}"

fail() { echo "foxify install: $*" >&2; exit 1; }

case "$(uname -s)" in
  Linux)  os="linux" ;;
  Darwin) os="osx" ;;
  *) fail "unsupported OS '$(uname -s)'. On Windows use install.ps1." ;;
esac

case "$(uname -m)" in
  x86_64|amd64)  arch="x64" ;;
  aarch64|arm64) arch="arm64" ;;
  *) fail "unsupported architecture '$(uname -m)'." ;;
esac

rid="$os-$arch"
case "$rid" in
  linux-x64|linux-arm64|osx-arm64) ;;
  *) fail "no prebuilt binary for $rid. Build from source: https://github.com/$REPO#build-from-source" ;;
esac

if [ -n "${FOXIFY_VERSION:-}" ]; then
  base_url="https://github.com/$REPO/releases/download/v${FOXIFY_VERSION#v}"
else
  base_url="https://github.com/$REPO/releases/latest/download"
fi

archive="foxify-$rid.tar.gz"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

download() {
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$1" -o "$2"
  elif command -v wget >/dev/null 2>&1; then
    wget -q "$1" -O "$2"
  else
    fail "curl or wget is required."
  fi
}

echo "Downloading $base_url/$archive"
download "$base_url/$archive" "$tmp/$archive" || fail "download failed."

if download "$base_url/SHA256SUMS" "$tmp/SHA256SUMS" 2>/dev/null; then
  expected="$(grep " $archive\$" "$tmp/SHA256SUMS" | cut -d' ' -f1)"
  if command -v sha256sum >/dev/null 2>&1; then
    actual="$(sha256sum "$tmp/$archive" | cut -d' ' -f1)"
  else
    actual="$(shasum -a 256 "$tmp/$archive" | cut -d' ' -f1)"
  fi
  [ "$expected" = "$actual" ] || fail "checksum mismatch for $archive."
fi

tar -xzf "$tmp/$archive" -C "$tmp"
mkdir -p "$INSTALL_DIR"
mv "$tmp/foxify" "$INSTALL_DIR/foxify"
chmod +x "$INSTALL_DIR/foxify"

echo "Installed $("$INSTALL_DIR/foxify" --version) to $INSTALL_DIR/foxify"
echo "Update later with: foxify update"

case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *) echo "Add $INSTALL_DIR to your PATH, e.g.: echo 'export PATH=\"$INSTALL_DIR:\$PATH\"' >> ~/.profile" ;;
esac
