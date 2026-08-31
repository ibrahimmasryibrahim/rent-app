using FileListPageCounter.Core.Models;

namespace FileListPageCounter.App.Infrastructure;

/// <summary>What the export dialog needs to know before it can ask the user anything.</summary>
public sealed class ExportRequest
{
    public required string FormatName { get; init; }

    /// <summary>True for Word, where a page is a real, countable thing.</summary>
    public required bool Paginated { get; init; }

    public required int EntryCount { get; init; }

    public required int TotalPages { get; init; }

    public required string Title { get; init; }

    public int FontSize { get; init; } = ReportOptions.DefaultFontSize;

    public int ColumnBlocks { get; init; } = 1;
}

/// <summary>What the user decided. Null from the dialog means they cancelled.</summary>
public sealed class ExportChoice
{
    public required string Title { get; init; }

    public required int FontSize { get; init; }

    public required int ColumnBlocks { get; init; }

    public required bool OpenWhenDone { get; init; }
}
