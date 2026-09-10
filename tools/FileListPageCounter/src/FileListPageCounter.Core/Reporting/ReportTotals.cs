using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>The three numbers every report summarises, counted once and shared by both writers.</summary>
public readonly record struct ReportTotals(int Files, long Pages, int Unknown)
{
    /// <summary>
    /// Totals are taken from the rows about to be printed, so they follow the user's edits —
    /// correct a page count in the table and every figure in the report moves with it.
    /// </summary>
    public static ReportTotals From(IReadOnlyList<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        long pages = 0;
        int unknown = 0;

        foreach (ReportRow row in rows)
        {
            if (row.PageCount.HasValue) pages += row.PageCount.Value;
            else unknown++;
        }

        return new ReportTotals(rows.Count, pages, unknown);
    }
}
