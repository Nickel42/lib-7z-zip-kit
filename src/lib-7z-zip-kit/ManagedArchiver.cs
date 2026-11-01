using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Zip;

namespace Lib7zZipKit;

/// <summary>
/// The pure .NET engine (SharpCompress): writes 7z (LZMA2, also for
/// <see cref="ArchiveLevel.Store"/>) and zip (Deflate, or stored),
/// and unpacks and tests through <see cref="ArchiveReader"/>. It needs no
/// native libraries, so it runs on every platform .NET does, Android and iOS
/// included. It is slower than <see cref="SevenZipEngine"/>, single-threaded,
/// and reads only zip, 7z, tar and gz.
/// </summary>
public sealed class ManagedArchiver : IArchiver
{
    public string EngineName => "managed";

    public void Compress(IReadOnlyList<string> inputs, string output, CompressOptions options,
        Action<double>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (inputs.Count == 0) throw new ArgumentException("Nothing to compress.", nameof(inputs));
        if (File.Exists(output)) throw new ArchiveException($"{output} already exists.");

        var entries = Entries(inputs, options.ContentsOnly);
        var total = Math.Max(1, entries.Sum(e => e.File?.Length ?? 0));
        long done = 0;
        var buffer = new byte[1 << 20];
        try
        {
            using (var stream = File.Create(output))
            using (var writer = OpenWriter(stream, options, total))
            {
                foreach (var (name, file) in entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (file is null)
                    {
                        writer.WriteDirectory(name, null);
                        continue;
                    }
                    using var source = new CountingStream(file.OpenRead(), n =>
                    {
                        done += n;
                        progress?.Invoke(Math.Min(1, (double)done / total));
                        token.ThrowIfCancellationRequested();
                    });
                    writer.Write(name, source, file.LastWriteTime);
                }
            }
            progress?.Invoke(1);
        }
        catch (Exception ex)
        {
            TryDelete(output);
            if (ex is OperationCanceledException or OutOfMemoryException or ArchiveException) throw;
            throw new ArchiveException($"Writing {Path.GetFileName(output)} failed: {ex.Message}");
        }
    }

    private static IWriter OpenWriter(Stream stream, CompressOptions options, long total)
    {
        var store = options.Level == ArchiveLevel.Store;
        if (options.Format == ArchiveFormat.Zip)
        {
            return new ZipWriter(stream, new ZipWriterOptions(store ? CompressionType.None : CompressionType.Deflate, (int)options.Level)
            {
                UseZip64 = total >= uint.MaxValue,
            });
        }
        // SharpCompress writes 7z only with LZMA or LZMA2, so Store becomes LZMA2 at its fastest.
        return new SevenZipWriter(stream, new SevenZipWriterOptions(CompressionType.LZMA2)
        {
            CompressionLevel = Math.Max(1, (int)options.Level),
        });
    }

    /// <summary>What goes in, as archive names (with / separators): files by name, folders with their contents.</summary>
    private static List<(string Name, FileInfo? File)> Entries(IReadOnlyList<string> inputs, bool contentsOnly)
    {
        var list = new List<(string, FileInfo?)>();
        foreach (var input in inputs.Select(Path.GetFullPath))
        {
            if (File.Exists(input))
            {
                list.Add((Path.GetFileName(input), new FileInfo(input)));
                continue;
            }
            if (!Directory.Exists(input)) throw new FileNotFoundException($"{input} doesn't exist.", input);

            var dir = new DirectoryInfo(input);
            var prefix = contentsOnly && inputs.Count == 1 ? "" : dir.Name + "/";
            if (prefix.Length > 0) list.Add((prefix.TrimEnd('/'), null));
            foreach (var item in dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).OrderBy(i => i.FullName, StringComparer.Ordinal))
            {
                var name = prefix + Path.GetRelativePath(input, item.FullName).Replace('\\', '/');
                list.Add((name, item as FileInfo));
            }
        }
        return list;
    }

    public void Extract(string archive, string outputDirectory, Action<double>? progress = null, CancellationToken token = default) =>
        ArchiveReader.Extract(archive, outputDirectory, progress, token);

    public void Test(string archive, Action<double>? progress = null, CancellationToken token = default) =>
        ArchiveReader.Test(archive, progress, token);

    public void Dispose() { }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A read-only stream that reports how many bytes were read from it.</summary>
    private sealed class CountingStream(Stream inner, Action<int> read) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            if (n > 0) read(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            if (n > 0) read(n);
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
