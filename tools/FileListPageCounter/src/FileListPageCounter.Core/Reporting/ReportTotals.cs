using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>The three numbers every report summarises, counted once and shared by both writers.</summary>
public readonly record struct ReportTotals(int Files, long Pages, int Unknown)
{
    public static ReportTotals From(IReadOnlyList<FileEntry> entries)
    {
        long pages = 0;
        int unknown = 0;

        foreach (FileEntry entry in entries)
        {
            if (entry.PageCount.HasValue) pages += entry.PageCount.Value;
            else unknown++;
        }

        return new ReportTotals(entries.Count, pages, unknown);
    }
}
