using System.Runtime.InteropServices;

namespace Lib7zZipKit;

/// <summary>
/// Raw deflate (RFC 1951, no zlib or gzip wrapper) through 7-Zip's own Deflate
/// encoder, for formats made of independently compressed blocks such as CSO.
/// At level 9 it makes smaller output than zlib's best setting, at a higher
/// CPU cost. Not thread-safe: give each worker thread its own deflater.
/// </summary>
public sealed class SevenZipDeflater : IDisposable
{
    private IntPtr _handle;

    static SevenZipDeflater() => NativeLibraries.Register(typeof(SevenZipDeflater).Assembly);

    /// <summary>Makes a deflater, with 7-Zip's library from <paramref name="sevenZipLibraryPath"/> or the default places.</summary>
    /// <param name="level">1 (fastest) to 9 (7-Zip's ultra setting: 10 passes, 128 fast bytes).</param>
    /// <exception cref="ArchiveException">A library is missing or too old to have the deflater.</exception>
    public SevenZipDeflater(int level = 9, string? sevenZipLibraryPath = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9);
        if (!IsAvailable(out var problem)) throw new ArchiveException(problem);
        var path = sevenZipLibraryPath ?? SevenZipEngine.FindSevenZipLibrary()
            ?? throw new ArchiveException($"7-Zip's library ({SevenZipEngine.SevenZipLibraryName}) was not found.");
        var status = Native.szk_deflater_create(path, level, out _handle, out var error);
        if (status != Native.Status.Ok)
            throw new ArchiveException($"7-Zip's Deflate encoder could not be made: {Marshal.PtrToStringUTF8(error) ?? status.ToString()}");
    }

    /// <summary>Whether this build of the native engine has the deflater, and why not when it hasn't.</summary>
    public static bool IsAvailable(out string problem)
    {
        try
        {
            // sevenzipkit builds from before the deflater have no szk_deflater_create.
            if (!NativeLibrary.TryGetExport(NativeLibrary.Load(NativeLibraries.SevenZipKit, typeof(SevenZipDeflater).Assembly, null),
                    "szk_deflater_create", out _))
            {
                problem = "This build of sevenzipkit has no Deflate encoder; rebuild lib-7z-zip-kit's native engine.";
                return false;
            }
        }
        catch (DllNotFoundException ex)
        {
            problem = $"sevenzipkit could not be loaded: {ex.Message}";
            return false;
        }
        if (SevenZipEngine.FindSevenZipLibrary() is null)
        {
            problem = $"7-Zip's library ({SevenZipEngine.SevenZipLibraryName}) was not found next to the app or in its runtimes folder.";
            return false;
        }
        problem = "";
        return true;
    }

    /// <summary>
    /// Compresses <paramref name="input"/> into <paramref name="output"/> and returns the
    /// compressed length, or 0 when the result would not fit in <paramref name="output"/>.
    /// </summary>
    public int Compress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        var status = Native.szk_deflate(_handle, in MemoryMarshal.GetReference(input), (uint)input.Length,
            ref MemoryMarshal.GetReference(output), (uint)output.Length, out var written);
        if (status == Native.Status.Ok) return (int)written;
        var message = status == Native.Status.BadArgs ? "invalid arguments" : Marshal.PtrToStringUTF8(Native.szk_deflater_last_error(_handle));
        throw new ArchiveException(string.IsNullOrWhiteSpace(message) ? "7-Zip's Deflate encoder reported an error." : message.Trim());
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        Native.szk_deflater_free(_handle);
        _handle = IntPtr.Zero;
    }

    private static class Native
    {
        private const string Lib = NativeLibraries.SevenZipKit;

        public enum Status { Ok = 0, Error = 1, Cancelled = 2, BadArgs = 3 }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Status szk_deflater_create([MarshalAs(UnmanagedType.LPUTF8Str)] string lib7zPath, int level,
            out IntPtr deflater, out IntPtr error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void szk_deflater_free(IntPtr deflater);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr szk_deflater_last_error(IntPtr deflater);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Status szk_deflate(IntPtr deflater, in byte input, uint inSize, ref byte output, uint outCapacity, out uint outSize);
    }
}
