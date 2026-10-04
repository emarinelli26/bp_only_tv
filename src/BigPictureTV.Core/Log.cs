namespace BigPictureTV.Core;

public interface ILog
{
    void Write(string message);
}

/// <summary>Writes timestamped lines to the console and to a log file.</summary>
public sealed class FileLog : ILog
{
    readonly string? _file;
    readonly bool _console;
    readonly object _gate = new();

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
            try { File.AppendAllText(_file, line + Environment.NewLine); } catch (IOException) { }
        }
    }
}
