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

    /// <summary>Row counts offered by the preview, alongside "تلقائي" and a free number.</summary>
    public static readonly int[] SuggestedRowsPerPage = { 10, 15, 20, 25, 30, 40, 50, 75, 100 };

    private int _rowsPerPage;

    /// <summary>
    /// How many table rows go on each printed page. Zero means "as many as fit", which is what
    /// Word would do on its own; any other value paginates the report to exactly that many rows
    /// so the printed pages match the preview line for line.
    /// </summary>
    public int RowsPerPage
    {
        get => _rowsPerPage;
        set => _rowsPerPage = value <= 0 ? 0 : Math.Min(value, 500);
    }

    private string _userName = string.Empty;

    /// <summary>
    /// The name of the person compiling the list, printed small at the foot of every page when
    /// <see cref="ShowUserName"/> is on. Empty by default: a report is unsigned unless its user
    /// chooses to sign it.
    /// </summary>
    public string UserName
    {
        get => _userName;
        set => _userName = (value ?? string.Empty).Trim();
    }

    public bool ShowUserName { get; set; }

    /// <summary>True only when there is actually a name to print.</summary>
    public bool HasUserSignature => ShowUserName && UserName.Length > 0;

    /// <summary>Point size of the signature line — small enough to stay out of the way.</summary>
    public const int SignatureFontSize = 9;
}
