using FileListPageCounter.Core.Diagnostics;

namespace FileListPageCounter.Core.Models;

/// <summary>Everything produced by one scan. Immutable once handed to the UI.</summary>
public sealed class ScanResult
{
    public static readonly ScanResult Empty = new(Array.Empty<FileEntry>(), TimeSpan.Zero, new ProcessingLog(), Array.Empty<string>());

    public ScanResult(
        IReadOnlyList<FileEntry> entries,
        TimeSpan elapsed,
        ProcessingLog log,
        IReadOnlyList<string> integrityViolations)
    {
        Entries = entries;
        Elapsed = elapsed;
        Log = log;
        IntegrityViolations = integrityViolations;
    }

    public IReadOnlyList<FileEntry> Entries { get; }

    public TimeSpan Elapsed { get; }

    public ProcessingLog Log { get; }

    /// <summary>Empty when every source file was byte-for-byte untouched (name, size, timestamps, attributes).</summary>
    public IReadOnlyList<string> IntegrityViolations { get; }

    public int TotalFiles => Entries.Count;

    public int TotalPages
    {
        get
        {
            long sum = 0;
            foreach (FileEntry e in Entries)
            {
                if (e.PageCount.HasValue) sum += e.PageCount.Value;
            }

            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }
    }

    public int UnknownCount
    {
        get
        {
            int n = 0;
            foreach (FileEntry e in Entries)
            {
                if (!e.PageCount.HasValue) n++;
            }

            return n;
        }
    }
}
