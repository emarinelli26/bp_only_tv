namespace BigPictureTV.Core;

public interface ILog
{
    void Write(string message);
}

/// <summary>
/// Writes timestamped lines to the console and to a log file. When the file
/// reaches <see cref="MaxBytes"/> it becomes the ".old.log" one (replacing the
/// previous), so the log never grows without end.
/// </summary>
public sealed class FileLog : ILog
{
    readonly string? _file;
    readonly bool _console;
    readonly object _gate = new();
    long _size = -1; // bytes in the file, read on the first write

    public long MaxBytes { get; init; } = 1_000_000;

    /// <summary>Where the previous log goes: BigPictureTV.log → BigPictureTV.old.log.</summary>
    public static string OldFile(string file) => Path.ChangeExtension(file, ".old.log");

    public FileLog(string? file, bool console = true)
    {
        _file = file;
        _console = console;
    }

    public void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        lock (_gate)
        {
            if (_console) Console.WriteLine(line);
            if (_file == null) return;
            try
            {
                if (_size < 0) _size = File.Exists(_file) ? new FileInfo(_file).Length : 0;
                if (_size >= MaxBytes)
                {
                    File.Move(_file, OldFile(_file), overwrite: true);
                    _size = 0;
                }
                string text = line + Environment.NewLine;
                File.AppendAllText(_file, text);
                _size += System.Text.Encoding.UTF8.GetByteCount(text);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Another BigPictureTV process (bptv, the script) may have it
                // open; read the size again next time.
                _size = -1;
            }
        }
    }
}
