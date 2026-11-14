# Changelog

## Unreleased

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
