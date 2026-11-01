using Android.App;
using Android.OS;
using Android.Widget;
using Lib7zZipKit;

namespace AndroidSmoke;

/// <summary>
/// Writes a small file, archives it as 7z and zip, tests and unpacks both, and
/// shows which engine ran: "7-Zip" when the native libraries are in the APK,
/// "managed" otherwise.
/// </summary>
[Activity(Label = "7z/zip library smoke test", MainLauncher = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        string text;
        try
        {
            var dir = Path.Combine(CacheDir!.AbsolutePath, "smoke");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var input = Path.Combine(dir, "hello.txt");
            File.WriteAllText(input, string.Concat(Enumerable.Repeat("hello from Android ", 1000)));

            using var archiver = Archiver.Create();
            var lines = new List<string> { $"Engine: {archiver.EngineName}" };
            foreach (var format in new[] { ArchiveFormat.SevenZip, ArchiveFormat.Zip })
            {
                var output = Path.Combine(dir, format == ArchiveFormat.Zip ? "hello.zip" : "hello.7z");
                archiver.Compress([input], output, new CompressOptions { Format = format });
                archiver.Test(output);
                archiver.Extract(output, Path.Combine(dir, "out-" + format));
                var m = ArchiveReader.List(output).Single();
                lines.Add($"{Path.GetFileName(output)}: {new FileInfo(output).Length:N0} bytes, {m.Name} {m.Method} CRC {m.Crc}");
            }
            text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            text = "Failed: " + ex;
        }
        SetContentView(new TextView(this) { Text = text });
    }
}
