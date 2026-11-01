# Third-party components

lib-7z-zip-kit's own code (the C# library and the `sevenzipkit` C interface in
`native/`) is under the MIT license (`LICENSE`). It builds on these components,
each under its own license. Apps that ship the native engine should include
7-Zip's and bit7z's license texts (`_deps/7zip-src/DOC/License.txt` and
`_deps/bit7z-src/LICENSE` after `scripts/fetch-sources.sh`).

## 7-Zip 26.04 (`7z.dll`, `7z.so`; `lib7z.so` on Android)

Copyright Igor Pavlov. GNU LGPL 2.1 or later, with some parts under the BSD
3-clause license. Built from the official source release
https://github.com/ip7z/7zip/releases/tag/26.04 (`7z2604-src.tar.xz`)
**without RAR support**: 7-Zip's RAR code is under the unRAR license
restriction, which GPL apps can't accept, so it is left out of the build
(`DISABLE_RAR=1` with the gcc/clang makefiles; on Windows the RAR objects are
removed from `Bundles/Format7zF/Arc.mak`, see `scripts/fetch-sources.sh`).
The unRAR restriction therefore does not apply to these builds.

7-Zip's library is loaded at run time from its own file, so under the LGPL an
app's users may replace it with their own build of the same source, or a later
compatible version.

## bit7z 4.1.0 (linked into `sevenzipkit`)

Copyright Riccardo Ostani, under the Mozilla Public License 2.0. Used
unmodified; its source is at https://github.com/rikyoz/bit7z/releases/tag/v4.1.0.

## SharpCompress 0.50.5 (NuGet dependency, the managed engine)

Copyright Adam Hathcock and contributors, MIT license.
https://github.com/adamhathcock/sharpcompress

## CPM.cmake stand-in (`native/cmake/cpm-offline`)

Not CPM itself: a few lines that stop bit7z's build from downloading
anything. Part of this project, MIT.
