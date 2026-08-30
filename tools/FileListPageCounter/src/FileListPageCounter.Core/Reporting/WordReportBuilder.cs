using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Writes a genuine Microsoft Word document (Open XML / DOCX) — not HTML renamed to .docx.
/// A4 portrait, right-to-left, Arial, a bordered three-column table whose header row repeats on
/// every page and whose rows never split across a page break, plus totals above and a summary
/// below. The only file this class ever writes is the report the user asked for.
/// </summary>
public static class WordReportBuilder
{
    // A4 portrait in twentieths of a point (twips).
    private const int PageWidthTwips = 11906;
    private const int PageHeightTwips = 16838;
    private const int MarginTwips = 1134;      // 2 cm on every side
    private const int HeaderFooterTwips = 567; // 1 cm

    private const int UsableWidthTwips = PageWidthTwips - (2 * MarginTwips); // 9638

    // Column widths, right to left on the page: م | اسم الملف | عدد الصفحات
    private const int IndexColumnWidth = 900;
    private const int PagesColumnWidth = 2000;
    private const int NameColumnWidth = UsableWidthTwips - IndexColumnWidth - PagesColumnWidth;

    private const string HeaderShading = "D9D9D9";

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

        MainDocumentPart mainPart = document.AddMainDocumentPart();

        AddStyles(mainPart, options);

        var body = new Body();
        mainPart.Document = new Document(body);

        int totalFiles = entries.Count;
        long totalPages = 0;
        int unknownCount = 0;

        foreach (FileEntry entry in entries)
        {
            if (entry.PageCount.HasValue) totalPages += entry.PageCount.Value;
            else unknownCount++;
        }

        body.AppendChild(Title(options));
        body.AppendChild(InfoLine($"{Strings.TotalFiles}: {Number(totalFiles)}", options));
        body.AppendChild(InfoLine($"{Strings.TotalPages}: {Number(totalPages)}", options));
        body.AppendChild(EmptyParagraph(options));

        body.AppendChild(BuildTable(entries, options));

        body.AppendChild(EmptyParagraph(options));
        body.AppendChild(InfoLine(Strings.Summary, options, bold: true));
        body.AppendChild(InfoLine($"{Strings.TotalFiles}: {Number(totalFiles)}", options));
        body.AppendChild(InfoLine($"{Strings.TotalPages}: {Number(totalPages)}", options));
        body.AppendChild(InfoLine($"{Strings.UnknownFiles}: {Number(unknownCount)}", options));

        string? footerId = options.IncludePageNumbers ? AddFooter(mainPart, options) : null;
        body.AppendChild(BuildSectionProperties(footerId));

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

    // ------------------------------------------------------------ paragraphs

    private static Paragraph Title(ReportOptions options) =>
        BuildParagraph(options.Title, options, bold: true, JustificationValues.Center, spaceAfter: 240);

    private static Paragraph InfoLine(string text, ReportOptions options, bool bold = false) =>
        BuildParagraph(text, options, bold, JustificationValues.Right, spaceAfter: 60);

    private static Paragraph EmptyParagraph(ReportOptions options) =>
        BuildParagraph(string.Empty, options, bold: false, JustificationValues.Right, spaceAfter: 0);

    private static Paragraph BuildParagraph(
        string text,
        ReportOptions options,
        bool bold,
        JustificationValues justification,
        int spaceAfter)
    {
        var properties = new ParagraphProperties(
            new BiDi(),
            new SpacingBetweenLines
            {
                After = spaceAfter.ToString(CultureInfo.InvariantCulture),
                Before = "0",
                Line = "276",
                LineRule = LineSpacingRuleValues.Auto
            },
            new Justification { Val = justification });

        var paragraph = new Paragraph(properties);

        if (text.Length > 0)
        {
            paragraph.AppendChild(BuildRun(text, options, bold));
        }

        return paragraph;
    }

    private static Run BuildRun(string text, ReportOptions options, bool bold)
    {
        var properties = new RunProperties(Fonts(options));

        if (bold)
        {
            properties.AppendChild(new Bold());
            properties.AppendChild(new BoldComplexScript());
        }

        properties.AppendChild(new FontSize { Val = HalfPoints(options.FontSize) });
        properties.AppendChild(new FontSizeComplexScript { Val = HalfPoints(options.FontSize) });
        properties.AppendChild(new RightToLeftText());

        return new Run(
            properties,
            new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    // ----------------------------------------------------------------- table

    private static Table BuildTable(IReadOnlyList<FileEntry> entries, ReportOptions options)
    {
        var table = new Table(
            new TableProperties(
                new BiDiVisual(),                                     // first column renders on the right
                new TableWidth { Width = UsableWidthTwips.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                new TableJustification { Val = TableRowAlignmentValues.Center },
                Borders(),
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableCellMarginDefault(
                    new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = (short)80, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = (short)80, Type = TableWidthValues.Dxa }),
                new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = true, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true }),
            new TableGrid(
                new GridColumn { Width = IndexColumnWidth.ToString(CultureInfo.InvariantCulture) },
                new GridColumn { Width = NameColumnWidth.ToString(CultureInfo.InvariantCulture) },
                new GridColumn { Width = PagesColumnWidth.ToString(CultureInfo.InvariantCulture) }));

        table.AppendChild(HeaderRow(options));

        foreach (FileEntry entry in entries)
        {
            table.AppendChild(DataRow(entry, options));
        }

        return table;
    }

    private static TableBorders Borders()
    {
        // Border width is in eighths of a point: 8 = 1 pt outer, 4 = 0.5 pt inner.
        return new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 8U, Color = "000000" },
            new BottomBorder { Val = BorderValues.Single, Size = 8U, Color = "000000" },
            new LeftBorder { Val = BorderValues.Single, Size = 8U, Color = "000000" },
            new RightBorder { Val = BorderValues.Single, Size = 8U, Color = "000000" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Color = "000000" },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Color = "000000" });
    }

    private static TableRow HeaderRow(ReportOptions options)
    {
        var row = new TableRow(
            new TableRowProperties(
                new CantSplit(),      // never break the header itself
                new TableHeader()));  // repeat this row at the top of every page

        row.AppendChild(Cell(Strings.ColumnIndex, IndexColumnWidth, options, bold: true, JustificationValues.Center, HeaderShading));
        row.AppendChild(Cell(Strings.ColumnFileName, NameColumnWidth, options, bold: true, JustificationValues.Center, HeaderShading));
        row.AppendChild(Cell(Strings.ColumnPages, PagesColumnWidth, options, bold: true, JustificationValues.Center, HeaderShading));

        return row;
    }

    private static TableRow DataRow(FileEntry entry, ReportOptions options)
    {
        var row = new TableRow(
            new TableRowProperties(
                new CantSplit())); // keep a row whole instead of splitting it over two pages

        row.AppendChild(Cell(Number(entry.Index), IndexColumnWidth, options, bold: false, JustificationValues.Center, shading: null));
        row.AppendChild(Cell(entry.DisplayName, NameColumnWidth, options, bold: false, JustificationValues.Right, shading: null));
        row.AppendChild(Cell(entry.PageCountText, PagesColumnWidth, options, bold: false, JustificationValues.Center, shading: null));

        return row;
    }

    private static TableCell Cell(
        string text,
        int widthTwips,
        ReportOptions options,
        bool bold,
        JustificationValues justification,
        string? shading)
    {
        var properties = new TableCellProperties(
            new TableCellWidth { Width = widthTwips.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });

        if (shading is not null)
        {
            properties.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shading });
        }

        properties.AppendChild(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        return new TableCell(
            properties,
            BuildParagraph(text, options, bold, justification, spaceAfter: 0));
    }

    // ---------------------------------------------------------------- layout

    private static SectionProperties BuildSectionProperties(string? footerId)
    {
        var section = new SectionProperties();

        if (footerId is not null)
        {
            section.AppendChild(new FooterReference { Type = HeaderFooterValues.Default, Id = footerId });
        }

        section.AppendChild(new PageSize
        {
            Width = (uint)PageWidthTwips,
            Height = (uint)PageHeightTwips,
            Orient = PageOrientationValues.Portrait,
            Code = 9U // A4
        });

        section.AppendChild(new PageMargin
        {
            Top = MarginTwips,
            Bottom = MarginTwips,
            Left = (uint)MarginTwips,
            Right = (uint)MarginTwips,
            Header = (uint)HeaderFooterTwips,
            Footer = (uint)HeaderFooterTwips,
            Gutter = 0U
        });

        section.AppendChild(new Columns { Space = "425" });
        section.AppendChild(new BiDi()); // right-to-left section

        return section;
    }

    private static string AddFooter(MainDocumentPart mainPart, ReportOptions options)
    {
        FooterPart footerPart = mainPart.AddNewPart<FooterPart>();

        var paragraphProperties = new ParagraphProperties(
            new BiDi(),
            new Justification { Val = JustificationValues.Center });

        var footerParagraph = new Paragraph(paragraphProperties);

        footerParagraph.AppendChild(BuildRun(Strings.PageOf + " ", options, bold: false));

        // The run inside the field is the cached result Word shows until it refreshes the field.
        footerParagraph.AppendChild(
            new SimpleField(BuildRun("1", options, bold: false)) { Instruction = " PAGE " });

        footerPart.Footer = new Footer(footerParagraph);
        footerPart.Footer.Save();

        return mainPart.GetIdOfPart(footerPart);
    }

    // --------------------------------------------------------------- helpers

    private static string HalfPoints(int pointSize) =>
        (pointSize * 2).ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
