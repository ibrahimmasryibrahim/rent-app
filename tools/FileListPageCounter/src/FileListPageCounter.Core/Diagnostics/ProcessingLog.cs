using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace FileListPageCounter.Core.Diagnostics;

public sealed record LogEntry(DateTime TimestampUtc, string Path, string Message);

/// <summary>
/// In-memory log of everything that went wrong during a scan. It is never written next to the
/// source files; the caller decides whether to persist it, and <see cref="LogFileLocation"/>
/// always resolves to the user's local application data folder.
/// </summary>
public sealed class ProcessingLog
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public void Add(string path, string message) =>
        _entries.Enqueue(new LogEntry(DateTime.UtcNow, path, message));

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public int Count => _entries.Count;

    public string Render()
    {
        var sb = new StringBuilder();
        foreach (LogEntry e in _entries)
        {
            sb.Append(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture))
              .Append("\t")
              .Append(e.Path)
              .Append("\t")
              .AppendLine(e.Message);
        }

        return sb.ToString();
    }

    /// <summary>
    /// %LOCALAPPDATA%\FileListPageCounter\logs\log-yyyyMMdd-HHmmss.txt — deliberately outside any source folder.
    /// </summary>
    public static string LogFileLocation()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileListPageCounter",
            "logs");
        Directory.CreateDirectory(dir);
        string name = "log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
        return Path.Combine(dir, name);
    }

    /// <summary>Writes the log to <paramref name="path"/> (defaults to the local app data folder) and returns the path.</summary>
    public string Save(string? path = null)
    {
        path ??= LogFileLocation();
        File.WriteAllText(path, Render(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }
}
