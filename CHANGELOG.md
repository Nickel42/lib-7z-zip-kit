# Changelog

## Unreleased

- **`SevenZipEngine`:** `Extract` and `Test` overloads with a `threads` cap on 7-Zip's
  decoder threads (0 keeps 7-Zip's default, every core), so parallel jobs don't each
  start one decoder thread per core for multi-block 7z, xz or bzip2 streams.
  `sevenzipkit` gains `szk_extract2` and `szk_test2`; `SevenZipEngine.SupportsThreadLimit`
  reports an older native build, which ignores the cap. Output is unchanged.
- **`SevenZipDeflater`:** a constructor that also takes Deflate `passes` and `fastBytes`
  (through the new `szk_deflater_create2`). Fewer passes run several times faster for a
  slightly larger output; the existing constructor still makes the same bytes.
  `SevenZipDeflater.SupportsTuning` reports an older native build.
- **`ArchiveReader`:** zip and gz (and tar.gz) entries inflate through .NET's own
  Deflate (System.IO.Compression, native zlib) instead of SharpCompress's managed port:
  about twice as fast to test or unpack. Only archives it reads byte for byte as
  SharpCompress does take this path (stored or Deflate zip entries without encryption,
  the first gzip member, as before); the rest stay with SharpCompress.
- **`SevenZipDeflater`:** raw deflate (RFC 1951) through 7-Zip's own Deflate encoder, for
  formats made of independently compressed blocks such as CSO. `sevenzipkit` gains
  `szk_deflater_create`, `szk_deflate`, `szk_deflater_free` and `szk_deflater_last_error`
  (the ABI version stays 1; `SevenZipDeflater.IsAvailable` reports an older native build).
- **Dependencies:** SharpCompress 0.50.5 → 0.50.9 (a security fix) and
  System.IO.Hashing 10.0.0 → 10.0.15 (servicing).

## 1.0.0-rc.1 (2026-10-31)

First release, extracted from NoIntrovert's archive engine.

- `SevenZipEngine`: create 7z and zip archives and extract or test anything 7-Zip
  reads, through 7-Zip 26.04 and bit7z 4.1.0 (`sevenzipkit`, formerly NoIntrovert's
  `noi_archive`). Native builds for win-x64, win-arm64, osx (universal), linux-x64,
  linux-arm64, android-arm64 and android-x64.
- `ManagedArchiver`: the same operations in pure .NET (SharpCompress), for platforms
  without the native engine.
- `ArchiveReader`: list (with stored CRCs and methods), extract and test zip, 7z, tar
  and gz in managed code; refuses RAR and path traversal.
- `ArchiveFiles`: archive and RAR detection, split-file (.001, .002, ...) joining.
