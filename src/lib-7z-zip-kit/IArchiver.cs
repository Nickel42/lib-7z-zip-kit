namespace Lib7zZipKit;

/// <summary>
/// Writes, unpacks and tests archives. Two engines implement it:
/// <see cref="SevenZipEngine"/> (7-Zip's own native code; fastest, reads every
/// format 7-Zip does) and <see cref="ManagedArchiver"/> (pure .NET, runs
/// anywhere .NET does). <see cref="Archiver.Create"/> picks one.
/// </summary>
/// <remarks>
/// Progress is reported as a fraction from 0 to 1, on the calling thread.
/// Cancelling throws <see cref="OperationCanceledException"/> and removes a
/// half-written archive.
/// </remarks>
public interface IArchiver : IDisposable
{
    /// <summary>"7-Zip" or "managed": which engine this is, for logs and messages.</summary>
    string EngineName { get; }

    /// <summary>
    /// Compresses <paramref name="inputs"/> (files by name, folders with their
    /// contents) into <paramref name="output"/>, which must not exist.
    /// </summary>
    void Compress(IReadOnlyList<string> inputs, string output, CompressOptions options,
        Action<double>? progress = null, CancellationToken token = default);

    /// <summary>Extracts every file of <paramref name="archive"/> into <paramref name="outputDirectory"/>, overwriting.</summary>
    void Extract(string archive, string outputDirectory, Action<double>? progress = null, CancellationToken token = default);

    /// <summary>Decompresses every file and checks its CRC, writing nothing. Throws <see cref="ArchiveException"/> when damaged.</summary>
    void Test(string archive, Action<double>? progress = null, CancellationToken token = default);
}

/// <summary>Chooses an archive engine.</summary>
public static class Archiver
{
    /// <summary>
    /// 7-Zip's native engine when its libraries are present, otherwise the
    /// managed one (with <paramref name="allowManagedFallback"/>).
    /// </summary>
    /// <exception cref="ArchiveException">The native engine can't load and the fallback isn't allowed.</exception>
    public static IArchiver Create(bool allowManagedFallback = true)
    {
        try
        {
            return new SevenZipEngine();
        }
        catch (ArchiveException) when (allowManagedFallback)
        {
            return new ManagedArchiver();
        }
    }
}
