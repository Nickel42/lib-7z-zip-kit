#!/bin/bash
# Builds 7-Zip's 7z.so and libsevenzipkit.dylib for macOS, each universal
# (arm64 + x86_64), into artifacts/native/osx.
#
#   scripts/build-mac.sh [7-Zip source] [bit7z source]
#
# Defaults to the trees scripts/fetch-sources.sh unpacks.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
src7z="$(cd "${1:-$root/_deps/7zip-src}" && pwd)"
srcbit7z="$(cd "${2:-$root/_deps/bit7z-src}" && pwd)"
out="$root/artifacts/native/osx"
build="$root/build/osx"
jobs="$(sysctl -n hw.ncpu)"
mkdir -p "$out"

# 7z.so: 7-Zip's full format library, one build per architecture, then merged.
bundle="$src7z/CPP/7zip/Bundles/Format7zF"
for arch in arm64 x64; do
    (cd "$bundle" && make -j"$jobs" -f "../../cmpl_mac_$arch.mak" DISABLE_RAR=1)
done
lipo -create "$bundle/b/m_arm64/7z.so" "$bundle/b/m_x64/7z.so" -output "$out/7z.so"

# libsevenzipkit.dylib: the C interface over bit7z.
cmake -S "$root/native" -B "$build" -DCMAKE_BUILD_TYPE=Release \
    "-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64" -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 \
    "-DSZK_BIT7Z_DIR=$srcbit7z" "-DSZK_7ZIP_DIR=$src7z"
cmake --build "$build" --parallel "$jobs"
cp "$build/libsevenzipkit.dylib" "$out/"

lipo -info "$out/7z.so" "$out/libsevenzipkit.dylib"
echo "Built $out/7z.so and $out/libsevenzipkit.dylib"
