namespace Lib7zZipKit;

/// <summary>
/// Telling archives apart by name and signature, and plain split files
/// (game.bin.001, .002, ...), which are joined back into one.
/// </summary>
public static class ArchiveFiles
{
    /// <summary>The archive extensions <see cref="ArchiveReader"/> unpacks.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".zip", ".7z", ".tar", ".gz", ".tgz"];

    public static bool IsArchive(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// RAR archives (and their .rNN / .partN.rar volumes), by name. They aren't
    /// supported: 7-Zip's RAR decoder is under the unRAR license restriction.
    /// </summary>
    public static bool IsRar(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".rar", StringComparison.OrdinalIgnoreCase)
            || (ext.Length == 4 && char.ToLowerInvariant(ext[1]) == 'r' && char.IsDigit(ext[2]) && char.IsDigit(ext[3]));
    }

    /// <summary>The message for a refused RAR archive.</summary>
    public const string RarNotSupported =
        "RAR archives aren't supported (the RAR decoder's unRAR license isn't compatible with the GPL). Repack it as 7z or zip.";

    /// <summary>By content, so a RAR renamed to .zip is caught too: "Rar!\x1A\x07".</summary>
    public static bool HasRarSignature(string path)
    {
        Span<byte> head = stackalloc byte[6];
        using var f = File.OpenRead(path);
        return f.Read(head) == 6 && head.SequenceEqual("Rar!\x1A\x07"u8);
    }

    /// <summary>True for the first part of a split file: name.ext.001.</summary>
    public static bool IsFirstSplitPart(string path) =>
        Path.GetExtension(path).Equals(".001", StringComparison.Ordinal);

    /// <summary>Later parts of a split file (.002, .003, ...), which the .001 brings in.</summary>
    public static bool IsSecondaryVolume(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Length == 4 && char.IsDigit(ext[1]) && char.IsDigit(ext[2]) && char.IsDigit(ext[3]) && ext != ".001";
    }

    /// <summary>All parts of a split file, in order, starting from the .001.</summary>
    public static List<string> SplitParts(string firstPart)
    {
        var stem = firstPart[..^4];
        var parts = new List<string>();
        for (var i = 1; i < 1000; i++)
        {
            var p = $"{stem}.{i:000}";
            if (!File.Exists(p)) break;
            parts.Add(p);
        }
        return parts;
    }

    /// <summary>Joins the parts of a split file into <paramref name="destinationDirectory"/>; returns the joined file.</summary>
    public static string JoinSplitParts(string firstPart, string destinationDirectory,
        Action<double>? progress = null, CancellationToken token = default)
    {
        var parts = SplitParts(firstPart);
        var target = Path.Combine(destinationDirectory, Path.GetFileName(firstPart[..^4]));
        var total = Math.Max(1, parts.Sum(p => new FileInfo(p).Length));
        long done = 0;
        var buffer = new byte[1 << 20];
        using var dst = File.Create(target);
        foreach (var part in parts)
        {
            using var src = File.OpenRead(part);
            int n;
            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                dst.Write(buffer, 0, n);
                done += n;
                progress?.Invoke((double)done / total);
                token.ThrowIfCancellationRequested();
            }
        }
        return target;
    }
}
