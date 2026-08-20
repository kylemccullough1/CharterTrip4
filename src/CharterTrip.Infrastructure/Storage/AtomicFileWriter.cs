namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// Writes a file in a way that can't leave a half-written result behind.
///
/// The trick: write everything to a temp file, force it to physical disk, and only then
/// rename it over the real one. A rename within a volume is atomic, so a reader either sees
/// the entire old file or the entire new one — never a truncated mess. Losing trip.json to a
/// crash mid-save would ruin a weekend, and this is about ten lines to prevent.
/// </summary>
public static class AtomicFileWriter
{
    public static void Write(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
