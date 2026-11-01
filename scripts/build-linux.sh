#!/bin/bash
# Builds 7-Zip's 7z.so and libsevenzipkit.so for the Linux machine running it
# (x64 or arm64) into artifacts/native/linux-<arch>.
#
#   scripts/build-linux.sh [7-Zip source] [bit7z source]
#
# Defaults to the trees scripts/fetch-sources.sh unpacks. Needs gcc/g++, make
# and CMake. Build on the oldest distribution the library should run on: the
# binaries need that glibc (and libstdc++) or newer.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
src7z="$(cd "${1:-$root/_deps/7zip-src}" && pwd)"
srcbit7z="$(cd "${2:-$root/_deps/bit7z-src}" && pwd)"
case "$(uname -m)" in
    x86_64) arch=x64 ;;
    aarch64 | arm64) arch=arm64 ;;
    *) echo "unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac
out="$root/artifacts/native/linux-$arch"
build="$root/build/linux-$arch"
jobs="$(nproc)"
mkdir -p "$out"

# 7z.so: 7-Zip's full format library, with its plain gcc makefile (portable C,
# no assembler needed), into Format7zF/b/g.
bundle="$src7z/CPP/7zip/Bundles/Format7zF"
(cd "$bundle" && make -j"$jobs" -f ../../cmpl_gcc.mak DISABLE_RAR=1)
cp "$bundle/b/g/7z.so" "$out/"

# libsevenzipkit.so: the C interface over bit7z.
cmake -S "$root/native" -B "$build" -DCMAKE_BUILD_TYPE=Release \
    "-DSZK_BIT7Z_DIR=$srcbit7z" "-DSZK_7ZIP_DIR=$src7z"
cmake --build "$build" --parallel "$jobs"
cp "$build/libsevenzipkit.so" "$out/"
strip --strip-unneeded "$out/libsevenzipkit.so"

file "$out/7z.so" "$out/libsevenzipkit.so"
echo "Built $out/7z.so and $out/libsevenzipkit.so"
