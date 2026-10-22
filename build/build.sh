#!/usr/bin/env bash
# Build script for Torrent7 (Linux/macOS hosts).
# Runs the tests, publishes single-file self-contained builds for
# win-x64, linux-x64 and linux-arm64, and packs them into dist/.
# Usage: bash build/build.sh [--skip-tests] [--version X.Y.Z]

set -euo pipefail

SKIP_TESTS=0
VERSION=""

while [ $# -gt 0 ]; do
    case "$1" in
        --skip-tests) SKIP_TESTS=1 ;;
        --version)
            shift
            VERSION="${1:-}"
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 1
            ;;
    esac
    shift
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CSPROJ="src/Torrent7/Torrent7.csproj"

if [ -z "$VERSION" ]; then
    if [ ! -f "$CSPROJ" ]; then
        echo "Missing $CSPROJ" >&2
        exit 1
    fi
    VERSION="$(grep -o '<Version>[^<]*</Version>' "$CSPROJ" | head -n 1 | sed -e 's#<Version>##' -e 's#</Version>##' || true)"
    if [ -z "$VERSION" ]; then
        echo "No <Version> found in $CSPROJ" >&2
        exit 1
    fi
fi

echo "Torrent7 build $VERSION"

# Step 1: tests
if [ "$SKIP_TESTS" -eq 0 ]; then
    echo "Running tests..."
    if ! dotnet run --project tests/Torrent7.Tests -c Release; then
        echo "Tests failed, not publishing." >&2
        exit 1
    fi
fi

# Step 2: publish
RIDS="win-x64 linux-x64 linux-arm64"

for rid in $RIDS; do
    echo "Publishing $rid..."
    dotnet publish "$CSPROJ" -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none \
        -p:Version="$VERSION" \
        -o "publish/$rid"
done

# Step 3: package
mkdir -p dist
ARTIFACTS=()

if command -v zip >/dev/null 2>&1; then
    WIN_OUT="dist/Torrent7-$VERSION-win-x64.zip"
    rm -f "$WIN_OUT"
    (cd "publish/win-x64" && zip -qr "$ROOT/$WIN_OUT" .)
else
    echo "zip not found, packing win-x64 as tar.gz instead."
    WIN_OUT="dist/Torrent7-$VERSION-win-x64.tar.gz"
    rm -f "$WIN_OUT"
    tar -czf "$WIN_OUT" -C "publish/win-x64" .
fi
ARTIFACTS+=("$WIN_OUT")

for rid in linux-x64 linux-arm64; do
    OUT="dist/Torrent7-$VERSION-$rid.tar.gz"
    rm -f "$OUT"
    tar -czf "$OUT" -C "publish/$rid" .
    ARTIFACTS+=("$OUT")
done

echo ""
echo "Artifacts:"
for file in "${ARTIFACTS[@]}"; do
    bytes="$(wc -c < "$file" | tr -d ' ')"
    mb="$(awk -v b="$bytes" 'BEGIN { printf "%.2f", b / 1048576 }')"
    echo "  $ROOT/$file  ($mb MB)"
done
