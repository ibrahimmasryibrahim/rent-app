using FileListPageCounter.Core.Common;

namespace FileListPageCounter.Core.Models;

/// <summary>Formatting options for the generated Word document.</summary>
public sealed class ReportOptions
{
    /// <summary>Font sizes offered by the UI.</summary>
    public static readonly int[] AllowedFontSizes = { 16, 18, 20, 22, 24 };

    public const int DefaultFontSize = 20;

    public string FontFamily { get; set; } = "Arial";

    private int _fontSize = DefaultFontSize;

    public int FontSize
    {
        get => _fontSize;
        set => _fontSize = Array.IndexOf(AllowedFontSizes, value) >= 0 ? value : DefaultFontSize;
    }

    private string _title = Strings.ReportTitle;

    /// <summary>Heading of the report. Falls back to the standard title when cleared.</summary>
    public string Title
    {
        get => _title;
        set => _title = string.IsNullOrWhiteSpace(value) ? Strings.ReportTitle : value.Trim();
    }

    private int _columnBlocks = 1;

    /// <summary>
    /// How many times the three columns repeat across the width of the page (1, 2 or 3).
    /// Two or three blocks turn a long thin list into a fraction of the pages.
    /// </summary>
    public int ColumnBlocks
    {
        get => _columnBlocks;
        set => _columnBlocks = Reporting.ReportLayout.NormalizeBlocks(value);
    }

    /// <summary>Adds a "صفحة X" footer for printing.</summary>
    public bool IncludePageNumbers { get; set; } = true;
}
