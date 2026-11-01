namespace Lib7zZipKit;

/// <summary>The archive formats the library writes.</summary>
public enum ArchiveFormat { SevenZip = 0, Zip = 1 }

/// <summary>Compression levels 7-Zip understands (its -mx values).</summary>
public enum ArchiveLevel { Store = 0, Fastest = 1, Fast = 3, Normal = 5, Maximum = 7, Ultra = 9 }

/// <summary>A failure reported by an archive engine: a damaged archive, a failed write, a missing library.</summary>
public sealed class ArchiveException(string message) : Exception(message);

/// <summary>How to write a new archive.</summary>
public sealed record CompressOptions
{
    public ArchiveFormat Format { get; init; } = ArchiveFormat.SevenZip;
    public ArchiveLevel Level { get; init; } = ArchiveLevel.Normal;

    /// <summary>Worker threads; 0 lets the engine decide (all cores). The managed engine ignores it.</summary>
    public int Threads { get; init; }

    /// <summary>7z only: one solid block for all files (smaller, slower to unpack one file).</summary>
    public bool Solid { get; init; } = true;

    /// <summary>With a single folder as input, store what is inside it rather than the folder itself.</summary>
    public bool ContentsOnly { get; init; }
}

/// <summary>
/// One file in an archive, how it's compressed there and its CRC32 from the
/// archive's headers (upper-case hex; null when the archive doesn't store one).
/// </summary>
/// <param name="Name">Path inside the archive, with / separators.</param>
/// <param name="Size">Unpacked size in bytes.</param>
/// <param name="Method">
/// The compression method's name: "LZMA" (LZMA and LZMA2 alike), "Deflate",
/// "None" (stored), "PPMd"… and "Unknown" for one the reader can't decode
/// (a 7z made with zstd, say).
/// </param>
/// <param name="Crc">CRC32 as 8 upper-case hex digits, or null.</param>
public sealed record ArchiveMember(string Name, long Size, string Method, string? Crc = null);
