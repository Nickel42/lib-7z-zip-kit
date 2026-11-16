using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.GZip;
using SharpCompress.Readers.Tar;
using SharpCompress.Readers.Zip;

namespace Lib7zZipKit;

/// <summary>
/// Inflates zip and gz entries with .NET's own Deflate (System.IO.Compression, which
/// runs native zlib), several times faster than SharpCompress's managed port, for
/// <see cref="ArchiveReader"/>. It takes only archives it reads byte for byte as
/// SharpCompress does; anything else stays with SharpCompress.
/// </summary>
internal static class SystemInflate
{
    /// <summary>
    /// Runs <paramref name="body"/> over the entries of a zip, gz or tar.gz that
    /// <paramref name="reader"/> (SharpCompress's reader for the file) recognised. False,
    /// having moved neither the reader nor read anything, when SharpCompress should do it.
    /// </summary>
    public static bool TryForEach(string archivePath, IReader reader, Action<double>? progress,
        Action<EntryInfo, Stream, Action<int>> body, CancellationToken token)
    {
        return reader switch
        {
            GZipReader => TryGzip(archivePath, reader, progress, body, token),
            TarReader => TryTarGz(archivePath, progress, body, token),
            ZipReader => TryZip(archivePath, progress, body, token),
            _ => false,
        };
    }

    /// <summary>
    /// A plain .gz. SharpCompress gives its one entry the stored name and CRC and reads
    /// only the first gzip member; a raw Deflate stream over the same bytes stops there too.
    /// </summary>
    private static bool TryGzip(string archivePath, IReader reader, Action<double>? progress,
        Action<EntryInfo, Stream, Action<int>> body, CancellationToken token)
    {
        using var file = File.OpenRead(archivePath);
        if (!SkipGzipHeader(file)) return false;
        if (!reader.MoveToNextEntry()) return true; // nothing SharpCompress would unpack either
        token.ThrowIfCancellationRequested();
        if (reader.Entry.IsDirectory) return true;
        ArchiveReader.Refuse(reader.Entry);
        using var src = new DeflateStream(file, CompressionMode.Decompress, leaveOpen: true);
        body(EntryInfo.Of(reader.Entry, sizeKnown: false), src, _ => Report(progress, file));
        return true;
    }

    /// <summary>
    /// A gzip-compressed tar: SharpCompress's own tar reader, fed the first gzip member's
    /// bytes (all SharpCompress reads of it) from .NET's Deflate.
    /// </summary>
    private static bool TryTarGz(string archivePath, Action<double>? progress,
        Action<EntryInfo, Stream, Action<int>> body, CancellationToken token)
    {
        using var file = File.OpenRead(archivePath);
        if (!SkipGzipHeader(file)) return false; // a plain or otherwise compressed tar
        using var tar = new DeflateStream(file, CompressionMode.Decompress, leaveOpen: true);
        // The reader factory, as TarReader alone can't start on a stream that doesn't seek.
        using var reader = ReaderFactory.OpenReader(tar, new ReaderOptions { LeaveStreamOpen = true });
        if (reader is not TarReader) return false;
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory) continue;
            ArchiveReader.Refuse(reader.Entry);
            using var src = reader.OpenEntryStream();
            body(EntryInfo.Of(reader.Entry), src, _ => Report(progress, file));
        }
        return true;
    }

    /// <summary>
    /// A zip whose every entry is stored or Deflate and unencrypted, and whose central
    /// directory .NET and SharpCompress read alike (names, sizes, CRCs, in order).
    /// </summary>
    private static bool TryZip(string archivePath, Action<double>? progress,
        Action<EntryInfo, Stream, Action<int>> body, CancellationToken token)
    {
        List<(EntryInfo Info, bool IsDirectory, long Packed)> entries;
        try
        {
            using var listing = ArchiveFactory.OpenArchive(archivePath);
            if (listing.Type != ArchiveType.Zip) return false;
            entries = [];
            foreach (var e in listing.Entries)
            {
                if (e.IsEncrypted || e.CompressionType is not (CompressionType.None or CompressionType.Deflate)) return false;
                entries.Add((EntryInfo.Of(e), e.IsDirectory, e.CompressedSize));
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return false;
        }

        using var file = File.OpenRead(archivePath);
        ZipArchive zip;
        try { zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { return false; }
        using (zip)
        {
            var native = zip.Entries;
            if (native.Count != entries.Count) return false;
            for (var i = 0; i < native.Count; i++)
            {
                var (info, _, packed) = entries[i];
                if (native[i].FullName != info.Key || native[i].Length != info.Size
                    || native[i].CompressedLength != packed || native[i].Crc32 != (uint)info.Crc)
                    return false;
            }

            for (var i = 0; i < native.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (entries[i].IsDirectory) continue;
                using var src = native[i].Open();
                body(entries[i].Info, src, _ => Report(progress, file));
            }
        }
        return true;
    }

    /// <summary>
    /// Moves past a gzip member's header (RFC 1952) to its Deflate data. False, leaving
    /// the position anywhere, when it isn't one this can read.
    /// </summary>
    internal static bool SkipGzipHeader(Stream s)
    {
        Span<byte> head = stackalloc byte[10];
        if (s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) != head.Length) return false;
        // Magic, method 8 (Deflate), and no reserved flag bits.
        if (head[0] != 0x1F || head[1] != 0x8B || head[2] != 8 || (head[3] & 0xE0) != 0) return false;
        var flags = head[3];
        if ((flags & 0x04) != 0) // FEXTRA: a 2-byte length, then that many bytes
        {
            Span<byte> len = stackalloc byte[2];
            if (s.ReadAtLeast(len, 2, throwOnEndOfStream: false) != 2) return false;
            if (!Skip(s, len[0] | (len[1] << 8))) return false;
        }
        if ((flags & 0x08) != 0 && !SkipZeroTerminated(s)) return false; // FNAME
        if ((flags & 0x10) != 0 && !SkipZeroTerminated(s)) return false; // FCOMMENT
        if ((flags & 0x02) != 0 && !Skip(s, 2)) return false; // FHCRC
        return true;
    }

    private static bool Skip(Stream s, int count)
    {
        for (; count > 0; count--)
            if (s.ReadByte() < 0) return false;
        return true;
    }

    private static bool SkipZeroTerminated(Stream s)
    {
        int b;
        while ((b = s.ReadByte()) > 0) { }
        return b == 0;
    }

    private static void Report(Action<double>? progress, FileStream file) =>
        progress?.Invoke((double)file.Position / Math.Max(1, file.Length));
}
