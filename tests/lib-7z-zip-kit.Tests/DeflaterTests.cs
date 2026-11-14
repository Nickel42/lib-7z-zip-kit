using System.IO.Compression;
using System.Text;
using Xunit;

namespace Lib7zZipKit.Tests;

/// <summary>7-Zip's Deflate encoder; each test passes trivially where the native engine isn't built.</summary>
public class DeflaterTests
{
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("deflate me, 7-Zip ", 400)));

    private static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        using var input = new DeflateStream(new MemoryStream(data.ToArray()), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void RoundTrips_WithDotNetInflate(int level)
    {
        if (!SevenZipDeflater.IsAvailable(out _)) return;
        using var deflater = new SevenZipDeflater(level);
        var buffer = new byte[Text.Length];
        var n = deflater.Compress(Text, buffer);
        Assert.InRange(n, 1, Text.Length / 4);
        Assert.Equal(Text, Inflate(buffer.AsSpan(0, n)));
    }

    [Fact]
    public void ReusedDeflater_GivesIndependentStreams()
    {
        if (!SevenZipDeflater.IsAvailable(out _)) return;
        using var deflater = new SevenZipDeflater();
        var buffer = new byte[4096];
        for (var seed = 0; seed < 5; seed++)
        {
            var block = new byte[2048];
            new Random(seed).NextBytes(block.AsSpan(0, 512)); // part random, part zeros
            var n = deflater.Compress(block, buffer);
            Assert.True(n > 0);
            Assert.Equal(block, Inflate(buffer.AsSpan(0, n)));
        }
    }

    [Fact]
    public void OutputTooSmall_ReturnsZero()
    {
        if (!SevenZipDeflater.IsAvailable(out _)) return;
        using var deflater = new SevenZipDeflater();
        var random = Scratch.Random(2048, 7);
        Assert.Equal(0, deflater.Compress(random, new byte[2048]));
    }

    [Fact]
    public void LevelOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SevenZipDeflater(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SevenZipDeflater(10));
    }
}
