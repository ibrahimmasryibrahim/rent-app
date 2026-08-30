using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.PageCounting;

/// <summary>
/// Counts the pages of one family of file types. Implementations must open source files only
/// through <see cref="Common.ReadOnlyFile"/> and must never throw — failures are reported as
/// <see cref="PageCountResult.Failed"/> so one bad file cannot stop a scan.
/// Adding a new format is a matter of implementing this and registering it.
/// </summary>
public interface IPageCounter
{
    /// <summary>Lower-cased extensions including the dot, e.g. ".pdf".</summary>
    IReadOnlyList<string> Extensions { get; }

    PageCountResult Count(string path, ScanOptions options, CancellationToken cancellationToken);
}
