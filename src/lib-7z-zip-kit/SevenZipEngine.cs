using System.Reflection;
using System.Runtime.InteropServices;

namespace Lib7zZipKit;

/// <summary>
/// 7z and zip archives through 7-Zip's own engine: sevenzipkit (a C
/// interface over bit7z) driving 7-Zip's library (7z.dll, 7z.so, or lib7z.so
/// on Android). One instance runs one operation at a time; give each worker
/// its own.
/// </summary>
/// <remarks>
/// Both native libraries ship in the NuGet package under runtimes/&lt;rid&gt;/native
/// and are found there or next to the app. Set <see cref="NativeDirectory"/>
/// (before first use) to load them from somewhere else.
/// </remarks>
public sealed class SevenZipEngine : IArchiver
{
    private IntPtr _handle;

    static SevenZipEngine() => NativeLibraries.Register(typeof(SevenZipEngine).Assembly);

    /// <summary>
    /// A folder holding sevenzipkit and 7-Zip's library, searched before the
    /// default locations. Set it before the first engine is made.
    /// </summary>
    public static string? NativeDirectory { get; set; }

    /// <summary>File name of 7-Zip's library on this platform.</summary>
    public static string SevenZipLibraryName =>
        OperatingSystem.IsWindows() ? "7z.dll" : OperatingSystem.IsAndroid() ? "lib7z.so" : "7z.so";

    /// <summary>
    /// Where 7-Zip's library was found, or null. On Android this is the bare
    /// file name: the system loader finds it among the app's own libraries.
    /// </summary>
    public static string? FindSevenZipLibrary()
    {
        if (OperatingSystem.IsAndroid() && NativeDirectory is null) return SevenZipLibraryName;
        return NativeLibraries.Candidates(SevenZipLibraryName).FirstOrDefault(File.Exists);
    }

    /// <summary>Loads the engine, with 7-Zip's library from <paramref name="sevenZipLibraryPath"/> or the default places.</summary>
    /// <exception cref="ArchiveException">A library is missing or the wrong version.</exception>
    public SevenZipEngine(string? sevenZipLibraryPath = null)
    {
        var path = sevenZipLibraryPath ?? FindSevenZipLibrary()
            ?? throw new ArchiveException($"7-Zip's library ({SevenZipLibraryName}) was not found next to the app or in its runtimes folder.");
        int abi;
        try { abi = Native.szk_abi_version(); }
        catch (DllNotFoundException ex) { throw new ArchiveException($"sevenzipkit could not be loaded: {ex.Message}"); }
        catch (EntryPointNotFoundException ex) { throw new ArchiveException($"sevenzipkit could not be loaded: {ex.Message}"); }
        if (abi != Native.AbiVersion)
            throw new ArchiveException($"sevenzipkit has interface version {abi}; this library needs {Native.AbiVersion}.");

        var status = Native.szk_open(path, out _handle, out var error);
        if (status != Native.Status.Ok)
            throw new ArchiveException($"7-Zip's library could not be loaded: {Marshal.PtrToStringUTF8(error) ?? status.ToString()}");
    }

    public string EngineName => "7-Zip";

    /// <summary>Whether the native engine can load, and why not when it can't.</summary>
    public static bool IsAvailable(out string problem)
    {
        try
        {
            using var _ = new SevenZipEngine();
            problem = "";
            return true;
        }
        catch (ArchiveException ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    public void Compress(IReadOnlyList<string> inputs, string output, CompressOptions options,
        Action<double>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Compress(options.Format, options.Level, options.Threads, options.Solid, inputs, output, options.ContentsOnly, progress, token);
    }

    /// <summary>
    /// Compresses <paramref name="inputs"/> (files by name, folders with their
    /// contents) into a new archive. With <paramref name="contentsOnly"/> and a
    /// single folder, stores what is inside the folder rather than the folder.
    /// </summary>
    public void Compress(ArchiveFormat format, ArchiveLevel level, int threads, bool solid,
        IReadOnlyList<string> inputs, string output, bool contentsOnly, Action<double>? progress, CancellationToken token)
    {
        if (inputs.Count == 0) throw new ArgumentException("Nothing to compress.", nameof(inputs));
        var options = new Native.CompressOptions
        {
            StructSize = (uint)Marshal.SizeOf<Native.CompressOptions>(),
            Format = (int)format,
            Level = (int)level,
            Threads = (uint)Math.Max(0, threads),
            Solid = solid ? 1 : 0,
            ContentsOnly = contentsOnly ? 1 : 0,
        };
        var pointers = inputs.Select(p => Marshal.StringToCoTaskMemUTF8(Path.GetFullPath(p))).ToArray();
        try
        {
            Run(callback => Native.szk_compress(_handle, ref options, pointers, pointers.Length, Path.GetFullPath(output), callback, IntPtr.Zero),
                progress, token, output);
        }
        finally
        {
            foreach (var p in pointers) Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>Extracts every item of any archive 7-Zip reads into <paramref name="outputDirectory"/>.</summary>
    public void Extract(string archive, string outputDirectory, Action<double>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(outputDirectory);
        Run(callback => Native.szk_extract(_handle, Path.GetFullPath(archive), Path.GetFullPath(outputDirectory), callback, IntPtr.Zero),
            progress, token, null);
    }

    /// <summary>Decompresses every item and checks its CRC, writing nothing.</summary>
    public void Test(string archive, Action<double>? progress = null, CancellationToken token = default) =>
        Run(callback => Native.szk_test(_handle, Path.GetFullPath(archive), callback, IntPtr.Zero), progress, token, null);

    private void Run(Func<Native.ProgressFn, Native.Status> call, Action<double>? progress, CancellationToken token, string? partialOutput)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        token.ThrowIfCancellationRequested();
        Native.ProgressFn callback = (_, done, total) =>
        {
            if (total > 0) progress?.Invoke(Math.Clamp((double)done / total, 0, 1));
            return token.IsCancellationRequested ? 0 : 1;
        };
        var status = call(callback);
        GC.KeepAlive(callback);

        if (status == Native.Status.Ok) return;
        if (partialOutput is not null) TryDelete(partialOutput);
        if (status == Native.Status.Cancelled) throw new OperationCanceledException(token);
        var message = status == Native.Status.BadArgs ? "invalid arguments" : Marshal.PtrToStringUTF8(Native.szk_last_error(_handle));
        throw new ArchiveException(string.IsNullOrWhiteSpace(message) ? "7-Zip reported an error." : message.Trim());
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        Native.szk_close(_handle);
        _handle = IntPtr.Zero;
    }

    private static class Native
    {
        public const int AbiVersion = 1;
        private const string Lib = NativeLibraries.SevenZipKit;

        static Native() => NativeLibraries.Register(typeof(Native).Assembly);

        public enum Status { Ok = 0, Error = 1, Cancelled = 2, BadArgs = 3 }

        [StructLayout(LayoutKind.Sequential)]
        public struct CompressOptions
        {
            public uint StructSize;
            public int Format;
            public int Level;
            public uint Threads;
            public int Solid;
            public int ContentsOnly;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int ProgressFn(IntPtr user, ulong done, ulong total);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int szk_abi_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Status szk_open([MarshalAs(UnmanagedType.LPUTF8Str)] string lib7zPath, out IntPtr archive, out IntPtr error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void szk_close(IntPtr archive);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr szk_last_error(IntPtr archive);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Status szk_compress(IntPtr archive, ref CompressOptions options, IntPtr[] inputs, int inputCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string outPath, ProgressFn progress, IntPtr user);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Status szk_extract(IntPtr archive, [MarshalAs(UnmanagedType.LPUTF8Str)] string archivePath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string outDir, ProgressFn progress, IntPtr user);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Status szk_test(IntPtr archive, [MarshalAs(UnmanagedType.LPUTF8Str)] string archivePath,
            ProgressFn progress, IntPtr user);
    }
}

/// <summary>Finds the native libraries: <see cref="SevenZipEngine.NativeDirectory"/>, next to the app, then runtimes/&lt;rid&gt;/native.</summary>
internal static class NativeLibraries
{
    public const string SevenZipKit = "sevenzipkit";
    private static int _registered;

    public static void Register(Assembly assembly)
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        NativeLibrary.SetDllImportResolver(assembly, (name, asm, searchPath) =>
        {
            if (name != SevenZipKit) return IntPtr.Zero;
            var file = OperatingSystem.IsWindows() ? "sevenzipkit.dll"
                : OperatingSystem.IsMacOS() ? "libsevenzipkit.dylib" : "libsevenzipkit.so";
            if (SevenZipEngine.NativeDirectory is { } dir && NativeLibrary.TryLoad(Path.Combine(dir, file), out var own)) return own;
            if (NativeLibrary.TryLoad(name, asm, searchPath, out var handle)) return handle;
            foreach (var path in Candidates(file))
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle)) return handle;
            return IntPtr.Zero;
        });
    }

    /// <summary>Places a native file may be, most specific first.</summary>
    public static IEnumerable<string> Candidates(string file)
    {
        if (SevenZipEngine.NativeDirectory is { } dir) yield return Path.Combine(dir, file);
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, file);
        foreach (var rid in RuntimeIds())
            yield return Path.Combine(baseDir, "runtimes", rid, "native", file);
    }

    private static IEnumerable<string> RuntimeIds()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            var other => other.ToString().ToLowerInvariant(),
        };
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsAndroid() ? "android" : "linux";
        yield return $"{os}-{arch}";
        yield return os;
    }
}
