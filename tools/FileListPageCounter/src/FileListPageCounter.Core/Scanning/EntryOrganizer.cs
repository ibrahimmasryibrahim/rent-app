using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Scanning;

/// <summary>
/// Applies the display rules to a set of results: drop unsupported types when asked, sort, and
/// number the rows. Kept separate from the scan itself so the UI can re-apply a filter or a sort
/// instantly without touching the disk again.
/// </summary>
public static class EntryOrganizer
{
    public static List<FileEntry> Organize(
        IEnumerable<FileEntry> entries,
        ScanOptions options,
        IComparer<string>? nameComparer = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        IComparer<string> comparer = nameComparer ?? new NaturalComparer();

        IEnumerable<FileEntry> filtered = options.IgnoreUnsupportedFiles
            ? entries.Where(static e => e.Status != PageCountStatus.Unsupported)
            : entries;

        List<FileEntry> ordered = options.SortMode switch
        {
            SortMode.FolderOrder => filtered.OrderBy(static e => e.DiscoveryOrder).ToList(),
            _ => filtered
                .OrderBy(e => e.DisplayName, comparer)
                .ThenBy(e => e.FullPath, comparer)
                .ToList()
        };

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Index = i + 1;
        }

        return ordered;
    }
}
