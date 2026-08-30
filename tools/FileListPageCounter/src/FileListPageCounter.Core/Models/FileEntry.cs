using System.Globalization;
using FileListPageCounter.Core.Common;

namespace FileListPageCounter.Core.Models;

/// <summary>One row of the result: a source file and what we learned about it, read-only.</summary>
public sealed class FileEntry
{
    /// <summary>1-based row number, assigned after sorting.</summary>
    public int Index { get; internal set; }

    /// <summary>Position in the original discovery/selection order (used by <see cref="SortMode.FolderOrder"/>).</summary>
    public int DiscoveryOrder { get; init; }

    public required string FullPath { get; init; }

    /// <summary>File name including the extension.</summary>
    public required string FileName { get; init; }

    /// <summary>File name with the last extension removed — this is what goes into the Word table.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Lower-cased extension including the dot, or an empty string.</summary>
    public required string Extension { get; init; }

    public long SizeBytes { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    public int? PageCount { get; internal set; }

    public PageCountStatus Status { get; internal set; }

    /// <summary>Reason the count failed, or a note about a fallback strategy. Never shown in the Word table.</summary>
    public string? Note { get; internal set; }

    /// <summary>What the Word table and the preview grid display in the "عدد الصفحات" column.</summary>
    public string PageCountText =>
        PageCount.HasValue
            ? PageCount.Value.ToString(CultureInfo.InvariantCulture)
            : Strings.Unknown;
}
