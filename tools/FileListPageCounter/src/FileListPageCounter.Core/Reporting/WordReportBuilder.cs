using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Writes a genuine Microsoft Word document (Open XML / DOCX) — not HTML renamed to .docx.
///
/// The layout follows the conventions of a printed management report: a title block closed by a
/// rule, a band of headline figures, then a quiet data table where an accent header and
/// alternating row bands carry the structure instead of heavy grid lines. A running header and
/// a page-numbered footer keep multi-page prints oriented.
/// </summary>
public static class WordReportBuilder
{
    // A4 portrait in twentieths of a point (twips).
    private const int PageWidthTwips = 11906;
    private const int PageHeightTwips = 16838;
    private const int SideMarginTwips = 1134;   // 2 cm
    private const int TopMarginTwips = 1247;    // 2.2 cm, leaving room for the running header
    private const int HeaderFooterTwips = 567;  // 1 cm

    private const int UsableWidthTwips = PageWidthTwips - (2 * SideMarginTwips); // 9638

    // The headline figures sit in three equal cells.
    private const int FigureColumnWidth = UsableWidthTwips / 3;

    public static void Build(string outputPath, IReadOnlyList<FileEntry> entries, ReportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using WordprocessingDocument document =
            WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);

        DocumentProperties.Stamp(document, options);

        MainDocumentPart mainPart = document.AddMainDocumentPart();
        AddStyles(mainPart, options);

        var body = new Body();
        mainPart.Document = new Document(body);

        ReportTotals totals = ReportTotals.From(entries);

        // ---- title block -------------------------------------------------
        body.AppendChild(TitleParagraph(options));
        body.AppendChild(MetaParagraph(options));
        body.AppendChild(Spacer(options, 200));

        // ---- headline figures --------------------------------------------
        body.AppendChild(FigureBand(totals, options));
        body.AppendChild(Spacer(options, 240));

        // ---- the data table ----------------------------------------------
        body.AppendChild(BuildTable(entries, options));

        // ---- closing summary ---------------------------------------------
        body.AppendChild(Spacer(options, 260));
        body.AppendChild(SectionHeading(Strings.Summary, options));
        body.AppendChild(SummaryLine($"{Strings.TotalFiles}: {Number(totals.Files)}", options));
        body.AppendChild(SummaryLine($"{Strings.TotalPages}: {Number(totals.Pages)}", options));
        body.AppendChild(SummaryLine($"{Strings.UnknownFiles}: {Number(totals.Unknown)}", options));

        string? headerId = AddHeader(mainPart, options);
        string? footerId = options.IncludePageNumbers ? AddFooter(mainPart, options) : null;
        body.AppendChild(BuildSectionProperties(headerId, footerId));

        mainPart.Document.Save();
    }

    // ---------------------------------------------------------------- styles

    private static void AddStyles(MainDocumentPart mainPart, ReportOptions options)
    {
        StyleDefinitionsPart stylePart = mainPart.AddNewPart<StyleDefinitionsPart>();

        stylePart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunPropertiesBaseStyle(
                        Fonts(options),
                        new DocumentFormat.OpenXml.Wordprocessing.Color { Val = ReportTheme.TextColor },
                        new FontSize { Val = HalfPoints(options.FontSize) },
                        new FontSizeComplexScript { Val = HalfPoints(options.FontSize) })),
                new ParagraphPropertiesDefault(
                    new ParagraphPropertiesBaseStyle(
                        new BiDi(),
                        new SpacingBetweenLines
                        {
                            After = "0",
                            Line = "276",
                            LineRule = LineSpacingRuleValues.Auto
                        }))));

        stylePart.Styles.Save();
    }

    private static RunFonts Fonts(ReportOptions options) => new()
    {
        Ascii = options.FontFamily,
        HighAnsi = options.FontFamily,
        ComplexScript = options.FontFamily,
        EastAsia = options.FontFamily
    };

    // ----------------------------------------------------------- title block

    private static Paragraph TitleParagraph(ReportOptions options) =>
        BuildParagraph(
            options.Title,
            new TextStyle(
                Size: ReportTheme.Step(options.FontSize, ReportTheme.TitleStep),
                Bold: true,
                Color: ReportTheme.Accent),
            options,
            JustificationValues.Center,
            spaceAfter: 60,
            bottomRule: true);

    /// <summary>
    /// The generation date, in the Gregorian calendar with invariant digits. Formatting it with
    /// the ar-SA culture would silently switch to the Hijri calendar, which is not what an
    /// archive index wants.
    /// </summary>
    private static Paragraph MetaParagraph(ReportOptions options)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture);

        return BuildParagraph(
            $"تاريخ الإنشاء: {stamp}",
            new TextStyle(
                Size: ReportTheme.Step(options.FontSize, ReportTheme.MetaStep),
                Bold: false,
                Color: ReportTheme.MutedColor),
            options,
            JustificationValues.Center,
            spaceAfter: 0);
    }

    // ------------------------------------------------------- headline figures

    private static Table FigureBand(ReportTotals totals, ReportOptions options)
    {
        var table = new Table(
            new TableProperties(
                new BiDiVisual(),
                new TableWidth { Width = Twips(UsableWidthTwips), Type = TableWidthUnitValues.Dxa },
                new TableJustification { Val = TableRowAlignmentValues.Center },
                new TableBorders(
                    Border<TopBorder>(6U, ReportTheme.BorderColor),
                    Border<BottomBorder>(6U, ReportTheme.BorderColor),
                    Border<LeftBorder>(6U, ReportTheme.BorderColor),
                    Border<RightBorder>(6U, ReportTheme.BorderColor),
                    Border<InsideVerticalBorder>(6U, ReportTheme.BorderColor)),
                new TableLayout { Type = TableLayoutValues.Fixed },
                CellMargins(top: 120, bottom: 120, side: 80),
                new TableLook { Val = "0000", FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true }),
            new TableGrid(
                new GridColumn { Width = Twips(FigureColumnWidth) },
                new GridColumn { Width = Twips(FigureColumnWidth) },
                new GridColumn { Width = Twips(FigureColumnWidth) }));

        var row = new TableRow(new TableRowProperties(new CantSplit()));

        row.AppendChild(FigureCell(Strings.TotalFiles, Number(totals.Files), options));
        row.AppendChild(FigureCell(Strings.TotalPages, Number(totals.Pages), options));
        row.AppendChild(FigureCell(Strings.UnknownFiles, Number(totals.Unknown), options));

        table.AppendChild(row);
        return table;
    }

    /// <summary>One headline figure: the number in accent, the label small and muted beneath it.</summary>
    private static TableCell FigureCell(string label, string value, ReportOptions options)
    {
        var properties = new TableCellProperties(
            new TableCellWidth { Width = Twips(FigureColumnWidth), Type = TableWidthUnitValues.Dxa },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = ReportTheme.PanelFill },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        var figure = BuildParagraph(
            value,
            new TextStyle(
                Size: ReportTheme.Step(options.FontSize, ReportTheme.FigureStep),
                Bold: true,
                Color: ReportTheme.Accent),
            options,
            JustificationValues.Center,
            spaceAfter: 40);

        var caption = BuildParagraph(
            label,
            new TextStyle(
                Size: ReportTheme.Step(options.FontSize, ReportTheme.CaptionStep),
                Bold: false,
                Color: ReportTheme.MutedColor),
            options,
            JustificationValues.Center,
            spaceAfter: 0);

        return new TableCell(properties, figure, caption);
    }

    // ----------------------------------------------------------------- table

    private static Table BuildTable(IReadOnlyList<FileEntry> entries, ReportOptions options)
    {
        int blocks = ReportLayout.NormalizeBlocks(options.ColumnBlocks);
        (int indexWidth, int nameWidth, int countWidth) = ReportLayout.BlockColumnWidths(UsableWidthTwips, blocks);

        var grid = new TableGrid();
        for (int block = 0; block < blocks; block++)
        {
            grid.AppendChild(new GridColumn { Width = Twips(indexWidth) });
            grid.AppendChild(new GridColumn { Width = Twips(nameWidth) });
            grid.AppendChild(new GridColumn { Width = Twips(countWidth) });
        }

        var table = new Table(
            new TableProperties(
                new BiDiVisual(),                                   // the first block renders on the right
                new TableWidth { Width = Twips(UsableWidthTwips), Type = TableWidthUnitValues.Dxa },
                new TableJustification { Val = TableRowAlignmentValues.Center },
                new TableBorders(
                    Border<TopBorder>(8U, ReportTheme.Accent),
                    Border<BottomBorder>(8U, ReportTheme.Accent),
                    Border<LeftBorder>(4U, ReportTheme.BorderColor),
                    Border<RightBorder>(4U, ReportTheme.BorderColor),
                    Border<InsideHorizontalBorder>(4U, ReportTheme.BorderColor),
                    Border<InsideVerticalBorder>(4U, ReportTheme.BorderColor)),
                new TableLayout { Type = TableLayoutValues.Fixed },
                CellMargins(top: 70, bottom: 70, side: 100),
                new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true }),
            grid);

        table.AppendChild(HeaderRow(options, blocks, indexWidth, nameWidth, countWidth));

        bool banded = false;
        foreach (FileEntry?[] cells in ReportLayout.Arrange(entries, blocks))
        {
            table.AppendChild(DataRow(cells, options, banded, indexWidth, nameWidth, countWidth));
            banded = !banded;
        }

        return table;
    }

    private static TableRow HeaderRow(ReportOptions options, int blocks, int indexWidth, int nameWidth, int countWidth)
    {
        var row = new TableRow(
            new TableRowProperties(
                new CantSplit(),                                                        // never break the header
                new TableRowHeight { Val = 460U, HeightType = HeightRuleValues.AtLeast },
                new TableHeader()));                                                    // repeat on every page

        var style = new TextStyle(options.FontSize, Bold: true, Color: ReportTheme.OnAccent);

        // Every block carries its own headings, so each one reads as a complete little table.
        for (int block = 0; block < blocks; block++)
        {
            row.AppendChild(Cell(Strings.ColumnIndex, indexWidth, style, options, JustificationValues.Center, ReportTheme.Accent));
            row.AppendChild(Cell(Strings.ColumnFileName, nameWidth, style, options, JustificationValues.Center, ReportTheme.Accent));
            row.AppendChild(Cell(Strings.ColumnPages, countWidth, style, options, JustificationValues.Center, ReportTheme.Accent));
        }

        return row;
    }

    private static TableRow DataRow(
        FileEntry?[] cells,
        ReportOptions options,
        bool banded,
        int indexWidth,
        int nameWidth,
        int countWidth)
    {
        var row = new TableRow(
            new TableRowProperties(
                new CantSplit(),  // keep a row whole rather than splitting it over two pages
                new TableRowHeight { Val = 340U, HeightType = HeightRuleValues.AtLeast }));

        string? fill = banded ? ReportTheme.BandFill : null;

        var normal = new TextStyle(options.FontSize, Bold: false, Color: ReportTheme.TextColor);
        var muted = new TextStyle(options.FontSize, Bold: false, Color: ReportTheme.MutedColor);

        foreach (FileEntry? entry in cells)
        {
            if (entry is null)
            {
                // The list ran out mid-row: keep the grid intact with empty cells.
                row.AppendChild(Cell(string.Empty, indexWidth, muted, options, JustificationValues.Center, fill));
                row.AppendChild(Cell(string.Empty, nameWidth, normal, options, JustificationValues.Right, fill));
                row.AppendChild(Cell(string.Empty, countWidth, normal, options, JustificationValues.Center, fill));
                continue;
            }

            // A row number is an ordinal, not a quantity — no thousands separator.
            row.AppendChild(Cell(
                entry.Index.ToString(CultureInfo.InvariantCulture),
                indexWidth,
                muted,
                options,
                JustificationValues.Center,
                fill));

            row.AppendChild(Cell(entry.DisplayName, nameWidth, normal, options, JustificationValues.Right, fill));

            // An undetermined count is stated plainly but never shouts: muted, not bold.
            row.AppendChild(Cell(
                entry.PageCountText,
                countWidth,
                entry.PageCount.HasValue ? normal : muted,
                options,
                JustificationValues.Center,
                fill));
        }

        return row;
    }

    private static TableCell Cell(
        string text,
        int widthTwips,
        TextStyle style,
        ReportOptions options,
        JustificationValues justification,
        string? shading)
    {
        var properties = new TableCellProperties(
            new TableCellWidth { Width = Twips(widthTwips), Type = TableWidthUnitValues.Dxa });

        if (shading is not null)
        {
            properties.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shading });
        }

        properties.AppendChild(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        return new TableCell(
            properties,
            BuildParagraph(text, style, options, justification, spaceAfter: 0));
    }

    // --------------------------------------------------------------- summary

    private static Paragraph SectionHeading(string text, ReportOptions options) =>
        BuildParagraph(
            text,
            new TextStyle(options.FontSize, Bold: true, Color: ReportTheme.Accent),
            options,
            JustificationValues.Right,
            spaceAfter: 80,
            bottomRule: true);

    private static Paragraph SummaryLine(string text, ReportOptions options) =>
        BuildParagraph(
            text,
            new TextStyle(options.FontSize, Bold: false, Color: ReportTheme.TextColor),
            options,
            JustificationValues.Right,
            spaceAfter: 60);

    private static Paragraph Spacer(ReportOptions options, int spaceAfter) =>
        BuildParagraph(
            string.Empty,
            new TextStyle(options.FontSize, Bold: false, Color: ReportTheme.TextColor),
            options,
            JustificationValues.Right,
            spaceAfter);

    // ------------------------------------------------------ paragraphs & runs

    /// <summary>Size in points, weight and colour of one run of text.</summary>
    private readonly record struct TextStyle(int Size, bool Bold, string Color);

    private static Paragraph BuildParagraph(
        string text,
        TextStyle style,
        ReportOptions options,
        JustificationValues justification,
        int spaceAfter,
        bool bottomRule = false)
    {
        var properties = new ParagraphProperties();

        // Schema order inside pPr: pBdr, bidi, spacing, ind, jc.
        if (bottomRule)
        {
            properties.AppendChild(new ParagraphBorders(
                new BottomBorder
                {
                    Val = BorderValues.Single,
                    Size = 6U,
                    Space = 4U,
                    Color = ReportTheme.AccentSoft
                }));
        }

        properties.AppendChild(new BiDi());
        properties.AppendChild(new SpacingBetweenLines
        {
            Before = "0",
            After = spaceAfter.ToString(CultureInfo.InvariantCulture),
            Line = "276",
            LineRule = LineSpacingRuleValues.Auto
        });
        properties.AppendChild(new Justification { Val = justification });

        var paragraph = new Paragraph(properties);

        if (text.Length > 0)
        {
            paragraph.AppendChild(BuildRun(text, style, options));
        }

        return paragraph;
    }

    private static Run BuildRun(string text, TextStyle style, ReportOptions options)
    {
        // Schema order inside rPr: rFonts, b, bCs, color, sz, szCs, rtl.
        var properties = new RunProperties(Fonts(options));

        if (style.Bold)
        {
            properties.AppendChild(new Bold());
            properties.AppendChild(new BoldComplexScript());
        }

        properties.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = style.Color });
        properties.AppendChild(new FontSize { Val = HalfPoints(style.Size) });
        properties.AppendChild(new FontSizeComplexScript { Val = HalfPoints(style.Size) });
        properties.AppendChild(new RightToLeftText());

        return new Run(
            properties,
            new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    // ------------------------------------------------------- header & footer

    private static string AddHeader(MainDocumentPart mainPart, ReportOptions options)
    {
        HeaderPart headerPart = mainPart.AddNewPart<HeaderPart>();

        var paragraph = BuildParagraph(
            options.Title,
            new TextStyle(
                Size: ReportTheme.Step(options.FontSize, ReportTheme.MetaStep),
                Bold: false,
                Color: ReportTheme.MutedColor),
            options,
            JustificationValues.Right,
            spaceAfter: 0,
            bottomRule: true);

        headerPart.Header = new Header(paragraph);
        headerPart.Header.Save();

        return mainPart.GetIdOfPart(headerPart);
    }

    private static string AddFooter(MainDocumentPart mainPart, ReportOptions options)
    {
        FooterPart footerPart = mainPart.AddNewPart<FooterPart>();

        var style = new TextStyle(
            Size: ReportTheme.Step(options.FontSize, ReportTheme.MetaStep),
            Bold: false,
            Color: ReportTheme.MutedColor);

        var paragraph = BuildParagraph(
            string.Empty,
            style,
            options,
            JustificationValues.Center,
            spaceAfter: 0);

        paragraph.AppendChild(BuildRun(Strings.PageOf + " ", style, options));

        // The run inside the field is the cached result Word shows until it refreshes the field.
        paragraph.AppendChild(
            new SimpleField(BuildRun("1", style, options)) { Instruction = " PAGE " });

        footerPart.Footer = new Footer(paragraph);
        footerPart.Footer.Save();

        return mainPart.GetIdOfPart(footerPart);
    }

    // ---------------------------------------------------------------- layout

    private static SectionProperties BuildSectionProperties(string? headerId, string? footerId)
    {
        var section = new SectionProperties();

        // Schema order inside sectPr: headerReference, footerReference, pgSz, pgMar, cols, bidi.
        if (headerId is not null)
        {
            section.AppendChild(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId });
        }

        if (footerId is not null)
        {
            section.AppendChild(new FooterReference { Type = HeaderFooterValues.Default, Id = footerId });
        }

        section.AppendChild(new PageSize
        {
            Width = (uint)PageWidthTwips,
            Height = (uint)PageHeightTwips,
            Orient = PageOrientationValues.Portrait,
            Code = (ushort)9 // the Windows paper-size code for A4
        });

        section.AppendChild(new PageMargin
        {
            Top = TopMarginTwips,
            Bottom = SideMarginTwips,
            Left = (uint)SideMarginTwips,
            Right = (uint)SideMarginTwips,
            Header = (uint)HeaderFooterTwips,
            Footer = (uint)HeaderFooterTwips,
            Gutter = 0U
        });

        section.AppendChild(new Columns { Space = "425" });
        section.AppendChild(new BiDi()); // right-to-left section

        return section;
    }

    // --------------------------------------------------------------- helpers

    private static TBorder Border<TBorder>(UInt32Value size, string color)
        where TBorder : BorderType, new() =>
        new() { Val = BorderValues.Single, Size = size, Color = color };

    private static TableCellMarginDefault CellMargins(int top, int bottom, int side) =>
        new(
            new TopMargin { Width = Twips(top), Type = TableWidthUnitValues.Dxa },
            new TableCellLeftMargin { Width = (short)side, Type = TableWidthValues.Dxa },
            new BottomMargin { Width = Twips(bottom), Type = TableWidthUnitValues.Dxa },
            new TableCellRightMargin { Width = (short)side, Type = TableWidthValues.Dxa });

    private static string Twips(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string HalfPoints(int pointSize) =>
        (pointSize * 2).ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
