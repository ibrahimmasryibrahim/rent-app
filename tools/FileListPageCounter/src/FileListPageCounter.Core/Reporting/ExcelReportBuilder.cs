using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Writes a genuine Microsoft Excel workbook (Open XML / XLSX) — not a CSV renamed to .xlsx.
///
/// The sheet is styled like the Word report and follows the same rules: gridlines off so the
/// table itself is the only structure on the page, an accent header row, alternating row bands
/// instead of vertical rules, and headline figures above the data. The header stays frozen while
/// scrolling and repeats on every printed page, an auto-filter makes the table sortable and
/// searchable, and page counts are written as real numbers so Excel can sum and chart them.
/// </summary>
public static class ExcelReportBuilder
{
    // Row layout: 1 title, 2 date, 3 blank, 4 figure labels, 5 figures, 6 blank, 7 header, 8.. data.
    private const uint TitleRow = 1;
    private const uint MetaRow = 2;
    private const uint FigureLabelRow = 4;
    private const uint FigureValueRow = 5;
    private const uint HeaderRow = 7;
    private const uint FirstDataRow = 8;

    // Style indexes into the stylesheet built by BuildStylesheet().
    private const uint StyleTitle = 1;
    private const uint StyleMeta = 2;
    private const uint StyleHeader = 3;
    private const uint StyleName = 4;
    private const uint StyleNumber = 5;
    private const uint StyleNameBanded = 6;
    private const uint StyleNumberBanded = 7;
    private const uint StyleTotalLabel = 8;
    private const uint StyleTotalValue = 9;
    private const uint StyleFigureLabel = 10;
    private const uint StyleFigureValue = 11;

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

        ReportTotals totals = ReportTotals.From(entries);

        uint lastDataRow = entries.Count > 0 ? FirstDataRow + (uint)entries.Count - 1 : HeaderRow;
        uint totalsFooterRow = lastDataRow + 1;
        uint creditRow = totalsFooterRow + 2;

        using SpreadsheetDocument document =
            SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);

        DocumentProperties.Stamp(document, options);

        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = BuildStylesheet(options);
        stylesPart.Stylesheet.Save();

        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = BuildWorksheet(entries, options, totals, lastDataRow, totalsFooterRow, creditRow);
        worksheetPart.Worksheet.Save();

        workbookPart.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = Strings.WorksheetName
        }));

        // The Excel equivalent of a repeating table header: print the header row on every page.
        workbookPart.Workbook.AppendChild(new DefinedNames(new DefinedName
        {
            Name = "_xlnm.Print_Titles",
            LocalSheetId = 0U,
            Text = $"'{Strings.WorksheetName}'!${HeaderRow}:${HeaderRow}"
        }));

        workbookPart.Workbook.Save();
    }

    // ------------------------------------------------------------- worksheet

    private static Worksheet BuildWorksheet(
        IReadOnlyList<FileEntry> entries,
        ReportOptions options,
        ReportTotals totals,
        uint lastDataRow,
        uint totalsFooterRow,
        uint creditRow)
    {
        var sheetData = new SheetData();

        // ---- title block --------------------------------------------------
        sheetData.AppendChild(TallRow(TitleRow, 34D, TextCell("A", TitleRow, options.Title, StyleTitle)));

        // Gregorian, invariant digits: the ar-SA culture would switch this to the Hijri calendar.
        string stamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture);
        sheetData.AppendChild(SingleCellRow(MetaRow, $"تاريخ الإنشاء: {stamp}", StyleMeta));

        // ---- headline figures ---------------------------------------------
        var labels = new Row { RowIndex = FigureLabelRow };
        labels.AppendChild(TextCell("A", FigureLabelRow, Strings.TotalFiles, StyleFigureLabel));
        labels.AppendChild(TextCell("B", FigureLabelRow, Strings.TotalPages, StyleFigureLabel));
        labels.AppendChild(TextCell("C", FigureLabelRow, Strings.UnknownFiles, StyleFigureLabel));
        sheetData.AppendChild(labels);

        var figures = new Row { RowIndex = FigureValueRow, Height = 26D, CustomHeight = true };
        figures.AppendChild(NumberCell("A", FigureValueRow, totals.Files, StyleFigureValue));
        figures.AppendChild(NumberCell("B", FigureValueRow, totals.Pages, StyleFigureValue));
        figures.AppendChild(NumberCell("C", FigureValueRow, totals.Unknown, StyleFigureValue));
        sheetData.AppendChild(figures);

        // ---- the data table -------------------------------------------------
        var header = new Row { RowIndex = HeaderRow, Height = 24D, CustomHeight = true };
        header.AppendChild(TextCell("A", HeaderRow, Strings.ColumnIndex, StyleHeader));
        header.AppendChild(TextCell("B", HeaderRow, Strings.ColumnFileName, StyleHeader));
        header.AppendChild(TextCell("C", HeaderRow, Strings.ColumnPages, StyleHeader));
        sheetData.AppendChild(header);

        uint rowIndex = FirstDataRow;
        bool banded = false;

        foreach (FileEntry entry in entries)
        {
            uint nameStyle = banded ? StyleNameBanded : StyleName;
            uint numberStyle = banded ? StyleNumberBanded : StyleNumber;

            var row = new Row { RowIndex = rowIndex };
            row.AppendChild(NumberCell("A", rowIndex, entry.Index, numberStyle));
            row.AppendChild(TextCell("B", rowIndex, entry.DisplayName, nameStyle));

            // A real number when we know it, the words "غير معروف" when we do not — so Excel can
            // still sum the column without a damaged file poisoning the total.
            row.AppendChild(entry.PageCount.HasValue
                ? NumberCell("C", rowIndex, entry.PageCount.Value, numberStyle)
                : TextCell("C", rowIndex, Strings.Unknown, numberStyle));

            sheetData.AppendChild(row);
            rowIndex++;
            banded = !banded;
        }

        if (entries.Count > 0)
        {
            var footer = new Row { RowIndex = totalsFooterRow, Height = 22D, CustomHeight = true };
            footer.AppendChild(TextCell("A", totalsFooterRow, string.Empty, StyleTotalLabel));
            footer.AppendChild(TextCell("B", totalsFooterRow, Strings.GrandTotal, StyleTotalLabel));
            footer.AppendChild(NumberCell("C", totalsFooterRow, totals.Pages, StyleTotalValue));
            sheetData.AppendChild(footer);
        }

        if (!string.IsNullOrWhiteSpace(options.DeveloperName))
        {
            sheetData.AppendChild(SingleCellRow(
                creditRow,
                $"{Strings.PreparedBy}: {options.DeveloperName}",
                StyleMeta));
        }

        // ---- sheet assembly -------------------------------------------------
        // Child order follows the schema: sheetPr, sheetViews, sheetFormatPr, cols, sheetData,
        // autoFilter, mergeCells, pageMargins, pageSetup.
        var worksheet = new Worksheet();

        worksheet.AppendChild(new SheetProperties(new PageSetupProperties { FitToPage = true }));

        var sheetView = new SheetView
        {
            RightToLeft = true,
            TabSelected = true,
            WorkbookViewId = 0U,
            ShowGridLines = false   // the table is the only structure the eye needs
        };

        sheetView.AppendChild(new Pane
        {
            VerticalSplit = HeaderRow,
            TopLeftCell = "A" + FirstDataRow.ToString(CultureInfo.InvariantCulture),
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen
        });

        worksheet.AppendChild(new SheetViews(sheetView));
        worksheet.AppendChild(new SheetFormatProperties { DefaultRowHeight = 18D, CustomHeight = true });

        worksheet.AppendChild(new Columns(
            new Column { Min = 1U, Max = 1U, Width = 9D, CustomWidth = true },
            new Column { Min = 2U, Max = 2U, Width = 62D, CustomWidth = true },
            new Column { Min = 3U, Max = 3U, Width = 18D, CustomWidth = true }));

        worksheet.AppendChild(sheetData);

        if (entries.Count > 0)
        {
            worksheet.AppendChild(new AutoFilter { Reference = $"A{HeaderRow}:C{lastDataRow}" });
        }

        var mergeCells = new MergeCells();
        mergeCells.AppendChild(new MergeCell { Reference = $"A{TitleRow}:C{TitleRow}" });
        mergeCells.AppendChild(new MergeCell { Reference = $"A{MetaRow}:C{MetaRow}" });

        if (!string.IsNullOrWhiteSpace(options.DeveloperName))
        {
            mergeCells.AppendChild(new MergeCell { Reference = $"A{creditRow}:C{creditRow}" });
        }

        mergeCells.Count = (uint)mergeCells.ChildElements.Count;
        worksheet.AppendChild(mergeCells);

        worksheet.AppendChild(new PrintOptions { HorizontalCentered = true });

        worksheet.AppendChild(new PageMargins
        {
            Left = 0.5D,
            Right = 0.5D,
            Top = 0.6D,
            Bottom = 0.6D,
            Header = 0.3D,
            Footer = 0.3D
        });

        worksheet.AppendChild(new PageSetup
        {
            PaperSize = 9U,                                   // A4
            Orientation = OrientationValues.Portrait,
            FitToWidth = 1U,
            FitToHeight = 0U
        });

        return worksheet;
    }

    // ---------------------------------------------------------------- cells

    private static Row SingleCellRow(uint rowIndex, string text, uint styleIndex)
    {
        var row = new Row { RowIndex = rowIndex };
        row.AppendChild(TextCell("A", rowIndex, text, styleIndex));
        return row;
    }

    private static Row TallRow(uint rowIndex, double height, Cell cell)
    {
        var row = new Row { RowIndex = rowIndex, Height = height, CustomHeight = true };
        row.AppendChild(cell);
        return row;
    }

    private static Cell TextCell(string column, uint rowIndex, string text, uint styleIndex) => new()
    {
        CellReference = column + rowIndex.ToString(CultureInfo.InvariantCulture),
        StyleIndex = styleIndex,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static Cell NumberCell(string column, uint rowIndex, long value, uint styleIndex) => new()
    {
        CellReference = column + rowIndex.ToString(CultureInfo.InvariantCulture),
        StyleIndex = styleIndex,
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    // --------------------------------------------------------------- styles

    private static Stylesheet BuildStylesheet(ReportOptions options)
    {
        double body = options.FontSize;
        double title = ReportTheme.Step(options.FontSize, ReportTheme.TitleStep);
        double figure = ReportTheme.Step(options.FontSize, ReportTheme.FigureStep);
        double caption = ReportTheme.Step(options.FontSize, ReportTheme.CaptionStep);
        double meta = ReportTheme.Step(options.FontSize, ReportTheme.MetaStep);

        var fonts = new Fonts(
            MakeFont(body, bold: false, ReportTheme.TextColor, options),   // 0 body
            MakeFont(body, bold: true, ReportTheme.OnAccent, options),     // 1 header, on the accent fill
            MakeFont(title, bold: true, ReportTheme.Accent, options),      // 2 title
            MakeFont(meta, bold: false, ReportTheme.MutedColor, options),  // 3 meta and credit
            MakeFont(figure, bold: true, ReportTheme.Accent, options),     // 4 headline figure
            MakeFont(caption, bold: false, ReportTheme.MutedColor, options), // 5 figure caption
            MakeFont(body, bold: true, ReportTheme.Accent, options))       // 6 grand total
        {
            Count = 7U
        };

        var fills = new Fills(
            // Excel reserves the first two fills; changing them corrupts the file.
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            SolidFill(ReportTheme.Accent),      // 2 header
            SolidFill(ReportTheme.BandFill),    // 3 alternating rows
            SolidFill(ReportTheme.PanelFill))   // 4 figure band and total row
        {
            Count = 5U
        };

        var borders = new Borders(
            NoBorder(),                                       // 0
            HairlineBox(),                                    // 1 data cells
            TopRule())                                        // 2 grand total
        {
            Count = 3U
        };

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat { NumberFormatId = 0U, FontId = 0U, FillId = 0U, BorderId = 0U })
        {
            Count = 1U
        };

        var cellFormats = new CellFormats(
            Format(fontId: 0U, fillId: 0U, borderId: 0U, HorizontalAlignmentValues.Right),                       // 0 default
            Format(fontId: 2U, fillId: 0U, borderId: 0U, HorizontalAlignmentValues.Center),                      // 1 title
            Format(fontId: 3U, fillId: 0U, borderId: 0U, HorizontalAlignmentValues.Center),                      // 2 meta
            Format(fontId: 1U, fillId: 2U, borderId: 1U, HorizontalAlignmentValues.Center, wrap: true),          // 3 header
            Format(fontId: 0U, fillId: 0U, borderId: 1U, HorizontalAlignmentValues.Right),                       // 4 name
            Format(fontId: 0U, fillId: 0U, borderId: 1U, HorizontalAlignmentValues.Center, numberFormat: 3U),    // 5 number
            Format(fontId: 0U, fillId: 3U, borderId: 1U, HorizontalAlignmentValues.Right),                       // 6 name, banded
            Format(fontId: 0U, fillId: 3U, borderId: 1U, HorizontalAlignmentValues.Center, numberFormat: 3U),    // 7 number, banded
            Format(fontId: 6U, fillId: 4U, borderId: 2U, HorizontalAlignmentValues.Center),                      // 8 total label
            Format(fontId: 6U, fillId: 4U, borderId: 2U, HorizontalAlignmentValues.Center, numberFormat: 3U),    // 9 total value
            Format(fontId: 5U, fillId: 4U, borderId: 0U, HorizontalAlignmentValues.Center),                      // 10 figure caption
            Format(fontId: 4U, fillId: 4U, borderId: 0U, HorizontalAlignmentValues.Center, numberFormat: 3U))    // 11 figure value
        {
            Count = 12U
        };

        var cellStyles = new CellStyles(
            new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
        {
            Count = 1U
        };

        return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
    }

    // Schema order inside a font: b, sz, color, name.
    private static Font MakeFont(double size, bool bold, string rgb, ReportOptions options)
    {
        var font = new Font();
        if (bold) font.AppendChild(new Bold());
        font.AppendChild(new FontSize { Val = size });
        font.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = "FF" + rgb });
        font.AppendChild(new FontName { Val = options.FontFamily });
        return font;
    }

    private static Fill SolidFill(string rgb) =>
        new(new PatternFill(
            new ForegroundColor { Rgb = "FF" + rgb },
            new BackgroundColor { Indexed = 64U })
        { PatternType = PatternValues.Solid });

    private static Border NoBorder() =>
        new(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder());

    /// <summary>A hairline box in the theme's border colour — present, but never loud.</summary>
    private static Border HairlineBox()
    {
        var rgb = "FF" + ReportTheme.BorderColor;
        return new Border(
            new LeftBorder(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = rgb }) { Style = BorderStyleValues.Thin },
            new RightBorder(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = rgb }) { Style = BorderStyleValues.Thin },
            new TopBorder(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = rgb }) { Style = BorderStyleValues.Thin },
            new BottomBorder(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = rgb }) { Style = BorderStyleValues.Thin },
            new DiagonalBorder());
    }

    /// <summary>A single strong rule above the total, the way a ledger closes a column.</summary>
    private static Border TopRule() =>
        new(
            new LeftBorder(),
            new RightBorder(),
            new TopBorder(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = "FF" + ReportTheme.Accent }) { Style = BorderStyleValues.Medium },
            new BottomBorder(),
            new DiagonalBorder());

    private static CellFormat Format(
        uint fontId,
        uint fillId,
        uint borderId,
        HorizontalAlignmentValues horizontal,
        bool wrap = false,
        uint numberFormat = 0U) =>
        new()
        {
            NumberFormatId = numberFormat,          // 3 = #,##0
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            ApplyNumberFormat = numberFormat != 0U,
            ApplyFont = true,
            ApplyFill = fillId != 0U,
            ApplyBorder = borderId != 0U,
            ApplyAlignment = true,
            Alignment = new Alignment
            {
                Horizontal = horizontal,
                Vertical = VerticalAlignmentValues.Center,
                WrapText = wrap
            }
        };
}
