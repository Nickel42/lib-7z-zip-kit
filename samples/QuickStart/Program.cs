using Lib7zZipKit;

// quickstart <folder|file>... <output.7z|output.zip>: archives the inputs, tests the
// result, lists it and unpacks it next to itself.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: quickstart <folder|file>... <output.7z|output.zip>");
    return 2;
}

var inputs = args[..^1];
var output = args[^1];
var format = output.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Zip : ArchiveFormat.SevenZip;

using var archiver = Archiver.Create();
Console.WriteLine($"Engine: {archiver.EngineName}");
archiver.Compress(inputs, output, new CompressOptions { Format = format, Level = ArchiveLevel.Normal },
    p => Console.Write($"\rCompressing {p:P0}"));
Console.WriteLine();
archiver.Test(output);
Console.WriteLine("Test: OK");

foreach (var m in ArchiveReader.List(output))
    Console.WriteLine($"  {m.Name,-40} {m.Size,12:N0}  {m.Method,-8} {m.Crc}");

var unpacked = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, Path.GetFileNameWithoutExtension(output) + "-unpacked");
archiver.Extract(output, unpacked);
Console.WriteLine($"Unpacked into {unpacked}");
return 0;
