namespace BigPictureTV.Core;

/// <summary>
/// Writes a file so that a crash or power cut mid-write never leaves it half
/// written: the text goes to a temporary file first, which then takes the
/// old one's place in one step.
/// </summary>
public static class SafeFile
{
    /// <summary>Where the previous version is kept when <c>keepBackup</c> is set: settings.json → settings.json.bak.</summary>
    public static string BackupOf(string file) => file + ".bak";

    public static void WriteAllText(string file, string text, bool keepBackup = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(text);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(file))
            File.Replace(temp, file, keepBackup ? BackupOf(file) : null, ignoreMetadataErrors: true);
        else
            File.Move(temp, file);
    }
}
