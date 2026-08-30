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

    public string Title { get; set; } = Strings.ReportTitle;

    /// <summary>Adds a "صفحة X من Y" footer for printing.</summary>
    public bool IncludePageNumbers { get; set; } = true;
}
