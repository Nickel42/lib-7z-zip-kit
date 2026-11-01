using System.Text;
using Xunit;

namespace Lib7zZipKit.Tests;

/// <summary>A scratch folder with a small tree of files to archive, removed afterwards.</summary>
public sealed class Scratch : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("szk-").FullName;
    public string Source => Path.Combine(Root, "src");

    public Scratch()
    {
        Directory.CreateDirectory(Path.Combine(Source, "sub", "deeper"));
        File.WriteAllText(Path.Combine(Source, "a.txt"), string.Concat(Enumerable.Repeat("hello archive ", 5000)));
        File.WriteAllBytes(Path.Combine(Source, "sub", "b.bin"), Random(300_000, 1));
        File.WriteAllBytes(Path.Combine(Source, "sub", "deeper", "c.bin"), Random(70_000, 2));
        File.WriteAllBytes(Path.Combine(Source, "empty.dat"), []);
    }

    public string Path_(string name) => Path.Combine(Root, name);

    public static byte[] Random(int size, int seed)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        return b;
    }

    /// <summary>Every file under <paramref name="dir"/>, by relative path (/ separators), with its bytes.</summary>
    public static Dictionary<string, byte[]> Tree(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes);

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

/// <summary>Runs a test body against each engine; the native one only where it is built.</summary>
public static class Engines
{
    public static IEnumerable<object[]> All()
    {
        yield return ["managed"];
        if (SevenZipEngine.IsAvailable(out _)) yield return ["7-Zip"];
    }

    public static IArchiver Make(string name) => name == "managed" ? new ManagedArchiver() : new SevenZipEngine();
}

public class RoundTripTests
{
    public static IEnumerable<object[]> Cases() =>
        from e in Engines.All()
        from f in new[] { ArchiveFormat.SevenZip, ArchiveFormat.Zip }
        from l in new[] { ArchiveLevel.Store, ArchiveLevel.Fast, ArchiveLevel.Ultra }
        select new object[] { e[0], f, l };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FolderContents_RoundTrip(string engine, ArchiveFormat format, ArchiveLevel level)
    {
        using var s = new Scratch();
        using var a = Engines.Make(engine);
        var output = s.Path_(format == ArchiveFormat.Zip ? "out.zip" : "out.7z");
        var seen = new List<double>();
        a.Compress([s.Source], output, new CompressOptions { Format = format, Level = level, ContentsOnly = true }, seen.Add);

        Assert.True(File.Exists(output));
        Assert.All(seen, p => Assert.InRange(p, 0, 1));
        a.Test(output);

        var dir = s.Path_("x");
        a.Extract(output, dir);
        AssertSameFiles(Scratch.Tree(s.Source), Scratch.Tree(dir));
    }

    [Theory]
    [MemberData(nameof(Engines.All), MemberType = typeof(Engines))]
    public void FolderInput_KeepsFolderName(string engine)
    {
        using var s = new Scratch();
        using var a = Engines.Make(engine);
        var output = s.Path_("named.7z");
        a.Compress([s.Source], output, new CompressOptions());
        var names = ArchiveReader.List(output).Select(m => m.Name).ToList();
        Assert.Contains("src/a.txt", names);
        Assert.Contains("src/sub/deeper/c.bin", names);
    }

    [Theory]
    [MemberData(nameof(Engines.All), MemberType = typeof(Engines))]
    public void FileInputs_StoredByName(string engine)
    {
        using var s = new Scratch();
        using var a = Engines.Make(engine);
        var output = s.Path_("files.zip");
        a.Compress([Path.Combine(s.Source, "a.txt"), Path.Combine(s.Source, "sub", "b.bin")], output,
            new CompressOptions { Format = ArchiveFormat.Zip });
        var members = ArchiveReader.List(output);
        Assert.Equal(["a.txt", "b.bin"], members.Select(m => m.Name).Order());
        Assert.All(members, m => Assert.Matches("^[0-9A-F]{8}$", m.Crc!));
        Assert.Equal(300_000, members.Single(m => m.Name == "b.bin").Size);
    }

    [Theory]
    [MemberData(nameof(Engines.All), MemberType = typeof(Engines))]
    public void Cancel_RemovesPartialArchive(string engine)
    {
        using var s = new Scratch();
        using var a = Engines.Make(engine);
        var output = s.Path_("cancel.7z");
        using var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            a.Compress([s.Source], output, new CompressOptions(), _ => cts.Cancel(), cts.Token));
        Assert.False(File.Exists(output));
    }

    [Theory]
    [MemberData(nameof(Engines.All), MemberType = typeof(Engines))]
    public void Test_FindsDamage(string engine)
    {
        using var s = new Scratch();
        using var a = Engines.Make(engine);
        var output = s.Path_("damaged.zip");
        a.Compress([Path.Combine(s.Source, "sub", "b.bin")], output, new CompressOptions { Format = ArchiveFormat.Zip, Level = ArchiveLevel.Store });
        var bytes = File.ReadAllBytes(output);
        bytes[bytes.Length / 2] ^= 0xFF; // inside the stored data
        File.WriteAllBytes(output, bytes);
        Assert.Throws<ArchiveException>(() => a.Test(output));
    }

    [Fact]
    public void Managed_ExistingOutput_Refused()
    {
        using var s = new Scratch();
        var output = s.Path_("exists.zip");
        File.WriteAllText(output, "x");
        Assert.Throws<ArchiveException>(() => new ManagedArchiver().Compress([s.Source], output, new CompressOptions { Format = ArchiveFormat.Zip }));
        Assert.Equal("x", File.ReadAllText(output));
    }

    [Fact]
    public void Archiver_Create_ReturnsAnEngine()
    {
        using var a = Archiver.Create();
        Assert.Contains(a.EngineName, new[] { "7-Zip", "managed" });
        Assert.Equal(SevenZipEngine.IsAvailable(out _) ? "7-Zip" : "managed", a.EngineName);
    }

    private static void AssertSameFiles(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (name, data) in expected) Assert.True(data.AsSpan().SequenceEqual(actual[name]), $"{name} differs");
    }
}

public class ReaderTests
{
    [Fact]
    public void Gz_ListsInnerName()
    {
        using var s = new Scratch();
        var gz = s.Path_("game.iso.gz");
        using (var f = File.Create(gz))
        using (var z = new System.IO.Compression.GZipStream(f, System.IO.Compression.CompressionLevel.Fastest))
            z.Write(Encoding.ASCII.GetBytes("disc image"));
        var m = Assert.Single(ArchiveReader.List(gz));
        Assert.Equal("game.iso", m.Name);
        Assert.Equal("GZip", m.Method);
    }

    [Fact]
    public void TryList_NullForGarbage()
    {
        using var s = new Scratch();
        var junk = s.Path_("junk.zip");
        File.WriteAllBytes(junk, Scratch.Random(1000, 3));
        Assert.Null(ArchiveReader.TryList(junk));
        Assert.Throws<ArchiveException>(() => ArchiveReader.List(junk));
    }

    [Fact]
    public void Extract_RefusesPathTraversal()
    {
        using var s = new Scratch();
        var zip = s.Path_("evil.zip");
        using (var f = File.Create(zip))
        using (var z = new System.IO.Compression.ZipArchive(f, System.IO.Compression.ZipArchiveMode.Create))
        using (var e = z.CreateEntry("../escape.txt").Open())
            e.Write("x"u8);
        Assert.Throws<InvalidDataException>(() => ArchiveReader.Extract(zip, s.Path_("out")));
        Assert.False(File.Exists(s.Path_("escape.txt")));
    }

    [Fact]
    public void Rar_RefusedByNameAndSignature()
    {
        using var s = new Scratch();
        var disguised = s.Path_("really-rar.zip");
        File.WriteAllBytes(disguised, [.. "Rar!\x1A\x07\x01\x00"u8, .. new byte[32]]);
        Assert.True(ArchiveFiles.HasRarSignature(disguised));
        Assert.Throws<NotSupportedException>(() => ArchiveReader.Extract(disguised, s.Path_("out")));
        Assert.True(ArchiveFiles.IsRar("a.rar"));
        Assert.True(ArchiveFiles.IsRar("a.r01"));
        Assert.False(ArchiveFiles.IsRar("a.zip"));
        Assert.False(ArchiveFiles.IsRar("d.iso.001"));
    }
}

public class SplitFileTests
{
    [Fact]
    public void JoinsPartsInOrder()
    {
        using var s = new Scratch();
        var data = Scratch.Random(25_000, 9);
        for (var i = 0; i < 3; i++)
            File.WriteAllBytes(s.Path_($"game.bin.{i + 1:000}"), data[(i * 10_000)..Math.Min(data.Length, (i + 1) * 10_000)]);

        var first = s.Path_("game.bin.001");
        Assert.True(ArchiveFiles.IsFirstSplitPart(first));
        Assert.True(ArchiveFiles.IsSecondaryVolume(s.Path_("game.bin.002")));
        Assert.False(ArchiveFiles.IsSecondaryVolume(first));
        Assert.Equal(3, ArchiveFiles.SplitParts(first).Count);

        var outDir = Directory.CreateDirectory(s.Path_("joined")).FullName;
        var joined = ArchiveFiles.JoinSplitParts(first, outDir);
        Assert.Equal("game.bin", Path.GetFileName(joined));
        Assert.Equal(data, File.ReadAllBytes(joined));
    }
}

public class NativeEngineTests
{
    [Fact]
    public void MissingLibrary_IsAnArchiveException()
    {
        var ex = Assert.Throws<ArchiveException>(() => new SevenZipEngine(Path.Combine(Path.GetTempPath(), "no-such-dir", SevenZipEngine.SevenZipLibraryName)));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void LibraryName_MatchesPlatform()
    {
        var expected = OperatingSystem.IsWindows() ? "7z.dll" : "7z.so";
        Assert.Equal(expected, SevenZipEngine.SevenZipLibraryName);
    }
}
