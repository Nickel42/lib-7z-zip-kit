#!/bin/bash
# Builds 7-Zip's library and libsevenzipkit.so for one Android ABI with the
# NDK, into artifacts/native/android-<arch>:
#
#   scripts/build-android.sh arm64|x64 [7-Zip source] [bit7z source]
#
# Needs ANDROID_NDK_HOME (NDK r27 or later), make and CMake; runs on Linux or
# macOS. Android only packages libraries named lib*.so into an APK, so 7-Zip's
# 7z.so is shipped as lib7z.so. Both libraries use 16 KB pages (Android 15+).
set -euo pipefail

arch="${1:-}"
case "$arch" in
    arm64) abi=arm64-v8a; triple=aarch64-linux-android; mak=cmpl_clang_arm64.mak ;;
    x64) abi=x86_64; triple=x86_64-linux-android; mak=cmpl_clang_x64.mak ;;
    *) echo "usage: build-android.sh arm64|x64 [7-Zip source] [bit7z source]" >&2; exit 2 ;;
esac
: "${ANDROID_NDK_HOME:?set ANDROID_NDK_HOME to the Android NDK}"
api=24

root="$(cd "$(dirname "$0")/.." && pwd)"
src7z="$(cd "${2:-$root/_deps/7zip-src}" && pwd)"
srcbit7z="$(cd "${3:-$root/_deps/bit7z-src}" && pwd)"
out="$root/artifacts/native/android-$arch"
build="$root/build/android-$arch"
host="$(uname -s | tr '[:upper:]' '[:lower:]')-x86_64"
toolchain="$ANDROID_NDK_HOME/toolchains/llvm/prebuilt/$host"
jobs="$(getconf _NPROCESSORS_ONLN)"
mkdir -p "$out"

# lib7z.so: 7-Zip's full format library with its clang makefile and the NDK's
# clang. Bionic has pthreads in libc (no -lpthread), and the assembler
# routines are left out, as on the other Unix builds.
bundle="$src7z/CPP/7zip/Bundles/Format7zF"
(cd "$bundle" && make -j"$jobs" -f "../../$mak" DISABLE_RAR=1 USE_ASM= \
    "CROSS_COMPILE=$toolchain/bin/$triple$api-" LIB2=-ldl \
    "LD_arch=-Wl,-z,max-page-size=16384 -static-libstdc++")
cp "$bundle"/b/c_*/7z.so "$out/lib7z.so"
"$toolchain/bin/llvm-strip" --strip-unneeded "$out/lib7z.so"

# libsevenzipkit.so: the C interface over bit7z, with libc++ linked in.
cmake -S "$root/native" -B "$build" -DCMAKE_BUILD_TYPE=Release \
    "-DCMAKE_TOOLCHAIN_FILE=$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
    "-DANDROID_ABI=$abi" "-DANDROID_PLATFORM=android-$api" -DANDROID_STL=c++_static \
    "-DSZK_BIT7Z_DIR=$srcbit7z" "-DSZK_7ZIP_DIR=$src7z"
cmake --build "$build" --parallel "$jobs"
cp "$build/libsevenzipkit.so" "$out/"
"$toolchain/bin/llvm-strip" --strip-unneeded "$out/libsevenzipkit.so"

file "$out/lib7z.so" "$out/libsevenzipkit.so"
echo "Built $out/lib7z.so and $out/libsevenzipkit.so"
