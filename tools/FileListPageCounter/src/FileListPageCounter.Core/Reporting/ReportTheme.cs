namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// One palette and one spacing scale, shared by the Word and the Excel writer so both files look
/// like they came from the same house. The rules behind the choices:
///
///   • A single accent colour carries the hierarchy; everything else is neutral.
///   • Borders are light and thin — whitespace separates content, not heavy rules.
///   • Alternating row bands do the work of vertical grid lines, so the table stays quiet.
///   • Type sizes step in a fixed ratio from the body size the user picked, never absolute,
///     so the hierarchy survives when the user changes the font size.
/// </summary>
public static class ReportTheme
{
    /// <summary>Deep navy — table headers and headline numbers.</summary>
    public const string Accent = "1F4E79";

    /// <summary>Lighter navy — rules and secondary emphasis.</summary>
    public const string AccentSoft = "2E75B6";

    public const string OnAccent = "FFFFFF";

    /// <summary>Very light blue-grey used for every other table row.</summary>
    public const string BandFill = "F2F6FA";

    /// <summary>The tint behind the summary figures.</summary>
    public const string PanelFill = "F7F9FC";

    public const string BorderColor = "BFCBD9";

    public const string TextColor = "1F2430";

    public const string MutedColor = "5A6B7C";

    // Type scale, expressed as points to add to (or subtract from) the chosen body size.
    public const int TitleStep = 4;
    public const int FigureStep = 6;
    public const int CaptionStep = -6;
    public const int MetaStep = -5;

    /// <summary>Keeps a derived size readable no matter what the user picked.</summary>
    public static int Step(int baseSize, int delta) => Math.Max(8, baseSize + delta);
}
