# lib-7z-zip-kit

Create, extract, test and list **7z** and **zip** archives from .NET, on
Windows, macOS, Linux and Android.

Two engines sit behind one interface, `IArchiver`:

| Engine | What it is | Where it runs |
| --- | --- | --- |
| `SevenZipEngine` | 7-Zip 26.04's own code through [bit7z](https://github.com/rikyoz/bit7z), via a small C interface (`sevenzipkit`). Multi-threaded; extracts and tests every format 7-Zip reads (except RAR). | win-x64, win-arm64, osx (universal), linux-x64, linux-arm64, android-arm64, android-x64 |
| `ManagedArchiver` | Pure .NET ([SharpCompress](https://github.com/adamhathcock/sharpcompress)). Writes 7z (LZMA2) and zip (Deflate); reads zip, 7z, tar, gz. | Anywhere .NET 8+ runs |

`Archiver.Create()` gives you the native engine when its libraries are there and
the managed one otherwise.

```csharp
using Lib7zZipKit;

using var archiver = Archiver.Create();
archiver.Compress(["Game (USA)"], "Game (USA).7z",
    new CompressOptions { Format = ArchiveFormat.SevenZip, Level = ArchiveLevel.Ultra, ContentsOnly = true },
    progress: p => Console.Write($"\r{p:P0}"));
archiver.Test("Game (USA).7z");                 // throws ArchiveException if damaged
archiver.Extract("Game (USA).7z", "unpacked");

foreach (var m in ArchiveReader.List("Game (USA).7z"))   // managed, nothing is unpacked
    Console.WriteLine($"{m.Name}  {m.Size}  {m.Method}  {m.Crc}");
```

Also included:

- `ArchiveReader` — `List` / `TryList` (names, sizes, methods and stored CRC32s),
  `Extract` and `Test` in managed code. Refuses RAR (by name and by signature),
  password-protected entries and entries that would land outside the target folder.
- `ArchiveFiles` — `IsArchive`, `IsRar`, `HasRarSignature`, and plain split files:
  `IsFirstSplitPart`, `SplitParts`, `JoinSplitParts` (`game.bin.001`, `.002`, ...).

Progress is a fraction from 0 to 1 reported on the calling thread. Cancelling
throws `OperationCanceledException` and removes a half-written archive. One
`SevenZipEngine` runs one operation at a time; give each worker thread its own.

## No RAR

7-Zip's RAR decoder is under the unRAR license restriction, which GPL apps can't
accept, so the native engine is built without it, and the managed reader refuses
RAR too. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Using it

### As a NuGet package

The package carries the native engine under `runtimes/<rid>/native` for every
platform above. .NET copies the right files next to your app; for Android they
go into the APK. Nothing else to set up.

### As a project reference (a checkout or git submodule)

Reference `src/lib-7z-zip-kit/lib-7z-zip-kit.csproj` and import the targets file
that copies the native engine for the platform being built:

```xml
<ProjectReference Include="lib-7z-zip-kit/src/lib-7z-zip-kit/lib-7z-zip-kit.csproj" />
<Import Project="lib-7z-zip-kit/build/lib-7z-zip-kit.Native.targets" />
```

It takes the files from `artifacts/native/<dir>` (build them as below), where
`<dir>` follows the `RuntimeIdentifier`, or the building machine without one.
`-p:SevenZipKitLibDir=<dir>` points it at any other build. The files stay beside
the app in a single-file publish, because bit7z loads 7-Zip's library by path.

To load the libraries from somewhere else at run time, set
`SevenZipEngine.NativeDirectory` before the first engine is made, or pass the
path of 7-Zip's library to `new SevenZipEngine(path)`.

## Building the native engine

```bash
bash scripts/fetch-sources.sh        # 7-Zip 26.04 + bit7z 4.1.0 into _deps/, SHA-256 checked
```

| Platform | Command | Output (`artifacts/native/…`) |
| --- | --- | --- |
| Windows x64 / ARM64 | `scripts\build-win.cmd x64` or `arm64` (Visual Studio C++ tools) | `win-x64/`, `win-arm64/`: `7z.dll`, `sevenzipkit.dll` |
| macOS | `scripts/build-mac.sh` (Xcode tools, CMake) | `osx/`: `7z.so`, `libsevenzipkit.dylib`, universal |
| Linux | `scripts/build-linux.sh` on x64 or arm64 (gcc, make, CMake) | `linux-x64/` or `linux-arm64/`: `7z.so`, `libsevenzipkit.so` |
| Android | `ANDROID_NDK_HOME=… scripts/build-android.sh arm64` or `x64` (NDK r27+, on Linux or macOS) | `android-arm64/`, `android-x64/`: `lib7z.so`, `libsevenzipkit.so` |

Build Linux binaries on the oldest distribution you support: they need its glibc
or newer. Android builds target API 24 and 16 KB pages.

## Tests

```bash
dotnet test lib-7z-zip-kit.sln
```

Every round-trip test runs against the managed engine, and against the native
one too when `artifacts/native/<dir>` for the machine has been built.

`samples/QuickStart` is a small console program; `samples/AndroidSmoke` is a
.NET for Android app that calls the library (`dotnet workload install android`).

## Continuous integration

`.github/workflows/ci.yml` builds the native engine for every platform, runs the
tests on Windows, macOS and Linux, builds the Android sample and packs the NuGet
package with all native builds. It runs only when started by hand
(`workflow_dispatch`); `release.yml` does the same for a `v*` tag and attaches
the package to a GitHub release.

## License

MIT for this project's own code. 7-Zip (LGPL 2.1+, parts BSD), bit7z (MPL 2.0) and
SharpCompress (MIT) keep their licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
