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

    // ---- what appears on the page -------------------------------------------------
    // Every one of these is a switch in the print window, because a list that is going into a
    // binder often wants nothing on it but the table itself.

    /// <summary>The large heading on the first page.</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>The "تاريخ الإنشاء" line under the heading.</summary>
    public bool ShowDateLine { get; set; } = true;

    /// <summary>The band of headline figures — how much was processed.</summary>
    public bool ShowTotalsBand { get; set; } = true;

    /// <summary>The quiet running title along the top of every page.</summary>
    public bool ShowRunningHeader { get; set; } = true;

    /// <summary>The closing summary block after the table.</summary>
    public bool ShowSummary { get; set; } = true;

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

    /// <summary>The name printed at the foot of the page unless the user changes or hides it.</summary>
    public const string DefaultUserName = "IBRAHIM MASRY IBRAHIM";

    private string _userName = DefaultUserName;

    /// <summary>
    /// The name printed small at the foot of every page. It is printed on its own — no label,
    /// no role, no "prepared by" — because the name is the whole of what is wanted there.
    /// </summary>
    public string UserName
    {
        get => _userName;
        set => _userName = (value ?? string.Empty).Trim();
    }

    public bool ShowUserName { get; set; } = true;

    /// <summary>True only when there is actually a name to print.</summary>
    public bool HasUserSignature => ShowUserName && UserName.Length > 0;

    /// <summary>Point size of the signature line — small enough to stay out of the way.</summary>
    public const int SignatureFontSize = 9;

    /// <summary>
    /// A separate copy, so the print window can try switches on and off against a live preview
    /// without touching the settings the main window is working from until the user keeps them.
    /// </summary>
    public ReportOptions Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        Title = Title,
        ColumnBlocks = ColumnBlocks,
        ShowTitle = ShowTitle,
        ShowDateLine = ShowDateLine,
        ShowTotalsBand = ShowTotalsBand,
        ShowRunningHeader = ShowRunningHeader,
        ShowSummary = ShowSummary,
        IncludePageNumbers = IncludePageNumbers,
        RowsPerPage = RowsPerPage,
        UserName = UserName,
        ShowUserName = ShowUserName
    };
}
