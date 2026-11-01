using System.IO.Hashing;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Lib7zZipKit;

/// <summary>
/// Reads zip, 7z, tar and gz archives in managed code (SharpCompress), with no
/// native libraries: lists their files, unpacks them and tests them. RAR is refused.
/// </summary>
public static class ArchiveReader
{
    /// <summary>
    /// The files in <paramref name="archive"/> (with / separators), how each is
    /// compressed and its stored CRC. Nothing is unpacked.
    /// </summary>
    /// <exception cref="ArchiveException">The list can't be read: damaged, password-protected headers, or an unknown type.</exception>
    public static IReadOnlyList<ArchiveMember> List(string archive)
    {
        List<ArchiveMember> members;
        try
        {
            using var opened = ArchiveFactory.OpenArchive(archive);
            members = opened.Entries.Where(e => !e.IsDirectory && e.Key is not null)
                .Select(e => new ArchiveMember(e.Key!.Replace('\\', '/'), e.Size, MethodOf(e), e.Crc == 0 ? null : ((uint)e.Crc).ToString("X8")))
                .ToList();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or ArchiveException))
        {
            throw new ArchiveException($"The file list of {Path.GetFileName(archive)} can't be read: {ex.Message}");
        }

        // A plain .gz holds one stream, often without a stored name: "game.iso.gz" holds "game.iso".
        if (Path.GetExtension(archive).Equals(".gz", StringComparison.OrdinalIgnoreCase) && members.Count <= 1)
            members = [new ArchiveMember(Path.GetFileNameWithoutExtension(archive), members.FirstOrDefault()?.Size ?? 0, "GZip")];
        return members;
    }

    /// <summary>Like <see cref="List"/>, but null when the list can't be read.</summary>
    public static IReadOnlyList<ArchiveMember>? TryList(string archive)
    {
        try { return List(archive); }
        catch (ArchiveException) { return null; }
    }

    /// <summary>
    /// The method's name: "LZMA" (LZMA and LZMA2 alike), "Deflate", "None"
    /// (stored), "PPMd"… and "Unknown" for one SharpCompress can't decode.
    /// </summary>
    private static string MethodOf(IArchiveEntry entry)
    {
        try { return entry.CompressionType.ToString(); }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException)) { return "Unknown"; }
    }

    /// <summary>
    /// Unpacks every file of a zip, 7z, tar, gz or tar.gz into <paramref name="destination"/>.
    /// Entries that would land outside it ("../" tricks) are refused.
    /// </summary>
    /// <exception cref="NotSupportedException">A RAR archive, or a password-protected entry.</exception>
    /// <exception cref="InvalidDataException">An entry points outside the destination.</exception>
    public static void Extract(string archivePath, string destination, Action<double>? progress = null, CancellationToken token = default)
    {
        if (ArchiveFiles.IsRar(archivePath) || ArchiveFiles.HasRarSignature(archivePath))
            throw new NotSupportedException(ArchiveFiles.RarNotSupported);
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var buffer = new byte[1 << 20];
        ForEachEntry(archivePath, progress, (entry, src, count) =>
        {
            // Refuse entries that would land outside the destination folder.
            var target = Path.GetFullPath(Path.Combine(root, entry.Key ?? "unnamed"));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry '{entry.Key}' points outside the extraction folder.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var dst = File.Create(target);
            int n;
            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                dst.Write(buffer, 0, n);
                count(n);
                token.ThrowIfCancellationRequested();
            }
        }, token);
        progress?.Invoke(1);
    }

    /// <summary>
    /// Decompresses every file and checks it against its stored CRC32 (where
    /// the format keeps one), writing nothing.
    /// </summary>
    /// <exception cref="ArchiveException">The archive is damaged or a CRC doesn't match.</exception>
    public static void Test(string archivePath, Action<double>? progress = null, CancellationToken token = default)
    {
        if (ArchiveFiles.IsRar(archivePath) || ArchiveFiles.HasRarSignature(archivePath))
            throw new NotSupportedException(ArchiveFiles.RarNotSupported);
        var buffer = new byte[1 << 20];
        try
        {
            ForEachEntry(archivePath, progress, (entry, src, count) =>
            {
                var crc = new Crc32();
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    crc.Append(buffer.AsSpan(0, n));
                    count(n);
                    token.ThrowIfCancellationRequested();
                }
                if (entry.Crc != 0 && crc.GetCurrentHashAsUInt32() != (uint)entry.Crc)
                    throw new ArchiveException($"CRC mismatch in {entry.Key}.");
            }, token);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or ArchiveException or NotSupportedException))
        {
            throw new ArchiveException(ex.Message);
        }
        progress?.Invoke(1);
    }

    /// <summary>Opens each file entry in archive order, so solid blocks are decompressed once.</summary>
    private static void ForEachEntry(string archivePath, Action<double>? progress, Action<IEntry, Stream, Action<int>> body, CancellationToken token)
    {
        // 7z needs random access, so it opens as an archive. Everything else streams
        // through a forward-only reader, which also handles tar.gz.
        if (Path.GetExtension(archivePath).Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var total = Math.Max(1, archive.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size));
            long done = 0;
            void Count(int n) { done += n; progress?.Invoke(Math.Min(1, (double)done / total)); }

            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;
                Refuse(reader.Entry);
                using var src = reader.OpenEntryStream();
                body(reader.Entry, src, Count);
            }
        }
        else
        {
            using var file = File.OpenRead(archivePath);
            using var reader = ReaderFactory.OpenReader(file, new ReaderOptions { LeaveStreamOpen = true });
            void Count(int _) => progress?.Invoke((double)file.Position / Math.Max(1, file.Length));
            while (reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;
                Refuse(reader.Entry);
                using var src = reader.OpenEntryStream();
                body(reader.Entry, src, Count);
            }
        }
    }

    private static void Refuse(IEntry entry)
    {
        // SharpCompress reports a 7z's empty files (which have no data stream) as encrypted.
        if (entry.IsEncrypted && entry.Size > 0)
            throw new NotSupportedException($"'{entry.Key}' in this archive is password protected.");
    }
}
