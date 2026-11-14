# sevenzipkit: lib-7z-zip-kit's C interface over bit7z (see sevenzipkit.h).
#
#   cmake -S native -B build/win-x64 -A x64 \
#         -DSZK_BIT7Z_DIR=<bit7z source> -DSZK_7ZIP_DIR=<7-Zip source>
#   cmake --build build/win-x64 --config Release
#
# Produces sevenzipkit.dll / libsevenzipkit.dylib / libsevenzipkit.so. 7-Zip's own library
# (7z.dll / 7z.so) is built separately from the same 7-Zip source and loaded
# at run time.

cmake_minimum_required(VERSION 3.20)
project(sevenzipkit LANGUAGES C CXX)

set(SZK_BIT7Z_DIR "" CACHE PATH "bit7z source tree (v4.1.0)")
set(SZK_7ZIP_DIR "" CACHE PATH "7-Zip source tree (26.04), the same one 7z.dll/7z.so is built from")
if(NOT EXISTS "${SZK_BIT7Z_DIR}/CMakeLists.txt")
  message(FATAL_ERROR "Set SZK_BIT7Z_DIR to the bit7z source tree")
endif()
if(NOT EXISTS "${SZK_7ZIP_DIR}/CPP/7zip")
  message(FATAL_ERROR "Set SZK_7ZIP_DIR to the 7-Zip source tree")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

# Static CRT on Windows, so the DLL needs no Visual C++ redistributable
# (7-Zip's own 7z.dll is built with /MT as well).
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# bit7z options: build against our 7-Zip copy, detect archive formats on
# extraction, and skip its tests, docs and install rules.
set(BIT7Z_CUSTOM_7ZIP_PATH "${SZK_7ZIP_DIR}" CACHE STRING "" FORCE)
set(BIT7Z_AUTO_FORMAT ON CACHE BOOL "" FORCE)
set(BIT7Z_GENERATE_PIC ON CACHE BOOL "" FORCE)
set(BIT7Z_STATIC_RUNTIME ON CACHE BOOL "" FORCE)
set(BIT7Z_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(BIT7Z_BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(BIT7Z_INSTALL OFF CACHE BOOL "" FORCE)

# bit7z bootstraps the CPM package manager by downloading it at configure
# time. With a custom 7-Zip path and std::filesystem available it fetches
# nothing through CPM, so a local stand-in keeps the build fully offline.
set(CPM_SOURCE_CACHE "${CMAKE_CURRENT_SOURCE_DIR}/cmake/cpm-offline" CACHE PATH "" FORCE)

# bit7z checks for std::filesystem by building and *running* a test program.
# Cross-compiling (ARM64 Windows on an x64 machine, or Android), the program
# can't run, so the check fails and bit7z would fall back to downloading
# ghc::filesystem. MSVC (since VS 2017) and the NDK's libc++ (since r22) have
# std::filesystem built in, so answer the check up front;
# check_cxx_source_runs skips a defined result.
if(MSVC OR ANDROID)
  set(STANDARD_FILESYSTEM_LINKS 1 CACHE INTERNAL "std::filesystem is in the standard library")
endif()

add_subdirectory("${SZK_BIT7Z_DIR}" bit7z EXCLUDE_FROM_ALL)

add_library(sevenzipkit SHARED sevenzipkit.cpp szk_deflate.cpp sevenzipkit.h)
target_link_libraries(sevenzipkit PRIVATE bit7z)
# szk_deflate.cpp uses 7-Zip's codec interfaces (ICoder.h) directly.
target_include_directories(sevenzipkit PRIVATE "${SZK_7ZIP_DIR}/CPP")
if(UNIX)
  target_link_libraries(sevenzipkit PRIVATE ${CMAKE_DL_LIBS})
endif()
target_compile_definitions(sevenzipkit PRIVATE BIT7Z_AUTO_FORMAT)

if(APPLE)
  set_target_properties(sevenzipkit PROPERTIES INSTALL_NAME_DIR "@rpath")
endif()
if(ANDROID)
  # 16 KB pages, which Android 15 devices may use.
  target_link_options(sevenzipkit PRIVATE "LINKER:-z,max-page-size=16384")
endif()
if(UNIX AND NOT APPLE)
  # Hidden visibility still leaves libstdc++'s template instantiations
  # exported; the version script keeps the library to szk_* only.
  target_link_options(sevenzipkit PRIVATE "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/sevenzipkit.map")
  set_target_properties(sevenzipkit PROPERTIES LINK_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/sevenzipkit.map")
endif()
