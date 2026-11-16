using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using Xunit;

namespace Lib7zZipKit.Tests;

/// <summary>
/// The managed reader inflates zip and gz with .NET's Deflate where it can. Each case
/// checks that path is taken (or not) and gives what SharpCompress alone gives.
/// </summary>
public class SystemInflateTests
{
    /// <summary>Whether <see cref="SystemInflate"/> takes the archive, reading it through.</summary>
    private static bool Taken(string archive)
    {
        using var file = File.OpenRead(archive);
        using var reader = ReaderFactory.OpenReader(file, new ReaderOptions { LeaveStreamOpen = true });
        return SystemInflate.TryForEach(archive, reader, null, (_, src, _) => src.CopyTo(Stream.Null), CancellationToken.None);
    }

    /// <summary>Extracts both ways and checks the files match; returns them.</summary>
    private static Dictionary<string, byte[]> ExtractBothWays(Scratch s, string archive)
    {
        var fast = s.Path_("fast-" + Path.GetFileName(archive));
        var slow = s.Path_("slow-" + Path.GetFileName(archive));
        ArchiveReader.Extract(archive, fast, null, default, systemInflate: true);
        ArchiveReader.Extract(archive, slow, null, default, systemInflate: false);
        var expected = Scratch.Tree(slow);
        var actual = Scratch.Tree(fast);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (name, data) in expected) Assert.True(data.AsSpan().SequenceEqual(actual[name]), $"{name} differs");
        return actual;
    }

    private static void WriteGzip(Stream to, byte[] data)
    {
        using var z = new GZipStream(to, CompressionLevel.Optimal, leaveOpen: true);
        z.Write(data);
    }

    [Fact]
    public void Zip_StoredAndDeflate()
    {
        using var s = new Scratch();
        var zip = s.Path_("mixed.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            z.CreateEntry("dir/");
            using (var e = z.CreateEntry("dir/text.txt", CompressionLevel.SmallestSize).Open())
                e.Write(File.ReadAllBytes(Path.Combine(s.Source, "a.txt")));
            using (var e = z.CreateEntry("stored.bin", CompressionLevel.NoCompression).Open())
                e.Write(Scratch.Random(70_000, 4));
            z.CreateEntry("empty.dat");
        }
        Assert.True(Taken(zip));
        var files = ExtractBothWays(s, zip);
        Assert.Equal(["dir/text.txt", "empty.dat", "stored.bin"], files.Keys.Order());
        ArchiveReader.Test(zip);
    }

    /// <summary>A zip written to a pipe (data descriptors, no sizes in the local headers) with non-ASCII names.</summary>
    [Fact]
    public void Zip_Streamed_WithDataDescriptorsAndUnicodeNames()
    {
        using var s = new Scratch();
        var zip = s.Path_("streamed.zip");
        var pipe = new MemoryStream();
        using (var z = new ZipArchive(new ForwardOnly(pipe), ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var e = z.CreateEntry("Pokémon (Japan)/disc 1.bin", CompressionLevel.Optimal).Open())
                e.Write(File.ReadAllBytes(Path.Combine(s.Source, "a.txt")));
            using (var e = z.CreateEntry("ゲーム.iso", CompressionLevel.NoCompression).Open())
                e.Write(Scratch.Random(50_000, 9));
        }
        File.WriteAllBytes(zip, pipe.ToArray());
        Assert.True(Taken(zip));
        var files = ExtractBothWays(s, zip);
        Assert.Equal(["Pokémon (Japan)/disc 1.bin", "ゲーム.iso"], files.Keys.Order(StringComparer.Ordinal));
        ArchiveReader.Test(zip);
    }

    /// <summary>A write-only stream that can't seek, so ZipArchive writes data descriptors.</summary>
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    [Fact]
    public void Zip_From7Zip()
    {
        if (!SevenZipEngine.IsAvailable(out _)) return;
        using var s = new Scratch();
        var zip = s.Path_("7z.zip");
        using (var e = new SevenZipEngine())
            e.Compress([s.Source], zip, new CompressOptions { Format = ArchiveFormat.Zip, ContentsOnly = true });
        Assert.True(Taken(zip));
        var files = ExtractBothWays(s, zip);
        Assert.Equal(Scratch.Tree(s.Source).Keys.Order(), files.Keys.Order());
    }

    [Fact]
    public void Zip_OtherMethod_StaysWithSharpCompress()
    {
        using var s = new Scratch();
        var zip = s.Path_("bzip2.zip");
        using (var f = File.Create(zip))
        using (var w = new ZipWriter(f, new ZipWriterOptions(CompressionType.BZip2)))
            w.Write("a.txt", Path.Combine(s.Source, "a.txt"));
        Assert.False(Taken(zip));
        ExtractBothWays(s, zip);
    }

    [Fact]
    public void Gz_NamedAndUnnamed()
    {
        using var s = new Scratch();
        var data = Scratch.Random(200_000, 5);
        var unnamed = s.Path_("disc.iso.gz");
        using (var f = File.Create(unnamed)) WriteGzip(f, data);

        // FNAME, FEXTRA and FCOMMENT set, which .NET's writer never does.
        var named = s.Path_("named.gz");
        using (var f = File.Create(named))
        {
            f.Write([0x1F, 0x8B, 8, 0x04 | 0x08 | 0x10, 0, 0, 0, 0, 0, 0xFF, 3, 0, 1, 2, 3]);
            f.Write("inner.iso\0a comment\0"u8);
            var body = new MemoryStream();
            WriteGzip(body, data);
            f.Write(body.ToArray().AsSpan(10)); // its Deflate data and trailer
        }

        foreach (var gz in new[] { unnamed, named })
        {
            Assert.True(Taken(gz));
            var file = Assert.Single(ExtractBothWays(s, gz));
            Assert.Equal(data, file.Value);
            ArchiveReader.Test(gz);
        }
        Assert.Equal("inner.iso", Assert.Single(Scratch.Tree(s.Path_("fast-named.gz"))).Key);
    }

    [Fact]
    public void Gz_SeveralMembers_ReadLikeSharpCompress()
    {
        using var s = new Scratch();
        var gz = s.Path_("members.bin.gz");
        using (var f = File.Create(gz))
        {
            WriteGzip(f, Scratch.Random(5000, 6));
            WriteGzip(f, Scratch.Random(5000, 7));
        }
        Assert.True(Taken(gz));
        ExtractBothWays(s, gz);
        // Its stored CRC is the last member's, so both ways call it damaged.
        Assert.Throws<ArchiveException>(() => ArchiveReader.Test(gz, null, default, systemInflate: true));
        Assert.Throws<ArchiveException>(() => ArchiveReader.Test(gz, null, default, systemInflate: false));
    }

    [Fact]
    public void Gz_Damaged_FailsTest()
    {
        using var s = new Scratch();
        var gz = s.Path_("damaged.bin.gz");
        using (var f = File.Create(gz)) WriteGzip(f, Scratch.Random(50_000, 8));
        var bytes = File.ReadAllBytes(gz);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(gz, bytes);
        Assert.Throws<ArchiveException>(() => ArchiveReader.Test(gz, null, default, systemInflate: true));
        Assert.Throws<ArchiveException>(() => ArchiveReader.Test(gz, null, default, systemInflate: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TarGz(bool twoMembers)
    {
        using var s = new Scratch();
        var tar = new MemoryStream();
        TarFile.CreateFromDirectory(s.Source, tar, includeBaseDirectory: false);
        var bytes = tar.ToArray();
        var tgz = s.Path_(twoMembers ? "two.tgz" : "one.tar.gz");
        using (var f = File.Create(tgz))
        {
            var split = twoMembers ? bytes.Length / 2 / 512 * 512 : bytes.Length;
            WriteGzip(f, bytes[..split]);
            if (twoMembers) WriteGzip(f, bytes[split..]);
        }
        Assert.True(Taken(tgz));
        var files = ExtractBothWays(s, tgz);
        if (!twoMembers)
        {
            foreach (var (name, data) in Scratch.Tree(s.Source)) Assert.Equal(data, files[name]);
            ArchiveReader.Test(tgz);
        }
    }

    [Fact]
    public void PlainTar_StaysWithSharpCompress()
    {
        using var s = new Scratch();
        var tar = s.Path_("plain.tar");
        TarFile.CreateFromDirectory(s.Source, tar, includeBaseDirectory: false);
        Assert.False(Taken(tar));
        ExtractBothWays(s, tar);
    }
}
