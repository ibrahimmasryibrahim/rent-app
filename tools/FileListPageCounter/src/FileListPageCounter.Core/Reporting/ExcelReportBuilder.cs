using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Writes a genuine Microsoft Excel workbook (Open XML / XLSX) — not a CSV renamed to .xlsx.
/// Right-to-left sheet, a bordered header that stays frozen while scrolling and repeats on every
/// printed page, an auto-filter for sorting and searching, a totals row, and A4 portrait print
/// setup. Page counts are written as real numbers so Excel can sum and chart them.
/// </summary>
public static class ExcelReportBuilder
{
    // Row layout: 1 title, 2 totals line, 3 blank, 4 header, 5.. data.
    private const uint TitleRow = 1;
    private const uint TotalsRow = 2;
    private const uint HeaderRow = 4;
    private const uint FirstDataRow = 5;

    // Style indexes into the stylesheet built by BuildStylesheet().
    private const uint StyleDefault = 0;
    private const uint StyleTitle = 1;
    private const uint StyleInfo = 2;
    private const uint StyleHeader = 3;
    private const uint StyleTextCell = 4;
    private const uint StyleNumberCell = 5;
    private const uint StyleTotalLabel = 6;
    private const uint StyleTotalValue = 7;

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

        var sheets = new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = Strings.WorksheetName
        });

        workbookPart.Workbook.AppendChild(sheets);

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

        sheetData.AppendChild(SingleCellRow(TitleRow, options.Title, StyleTitle));

        string totalsLine =
            $"{Strings.TotalFiles}: {Number(totals.Files)}   |   " +
            $"{Strings.TotalPages}: {Number(totals.Pages)}   |   " +
            $"{Strings.UnknownFiles}: {Number(totals.Unknown)}";

        sheetData.AppendChild(SingleCellRow(TotalsRow, totalsLine, StyleInfo));

        var header = new Row { RowIndex = HeaderRow };
        header.AppendChild(TextCell("A", HeaderRow, Strings.ColumnIndex, StyleHeader));
        header.AppendChild(TextCell("B", HeaderRow, Strings.ColumnFileName, StyleHeader));
        header.AppendChild(TextCell("C", HeaderRow, Strings.ColumnPages, StyleHeader));
        sheetData.AppendChild(header);

        uint rowIndex = FirstDataRow;
        foreach (FileEntry entry in entries)
        {
            var row = new Row { RowIndex = rowIndex };
            row.AppendChild(NumberCell("A", rowIndex, entry.Index, StyleNumberCell));
            row.AppendChild(TextCell("B", rowIndex, entry.DisplayName, StyleTextCell));

            // A real number when we know it, the words "غير معروف" when we do not — so Excel can
            // still sum the column without a damaged file poisoning the total.
            row.AppendChild(entry.PageCount.HasValue
                ? NumberCell("C", rowIndex, entry.PageCount.Value, StyleNumberCell)
                : TextCell("C", rowIndex, Strings.Unknown, StyleNumberCell));

            sheetData.AppendChild(row);
            rowIndex++;
        }

        if (entries.Count > 0)
        {
            var footer = new Row { RowIndex = totalsFooterRow };
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
                StyleInfo));
        }

        var worksheet = new Worksheet();

        // Child order follows the schema: sheetPr, sheetViews, sheetFormatPr, cols, sheetData,
        // autoFilter, mergeCells, pageMargins, pageSetup.
        worksheet.AppendChild(new SheetProperties(new PageSetupProperties { FitToPage = true }));

        var sheetView = new SheetView
        {
            RightToLeft = true,
            TabSelected = true,
            WorkbookViewId = 0U
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
            new Column { Min = 1U, Max = 1U, Width = 8D, CustomWidth = true },
            new Column { Min = 2U, Max = 2U, Width = 60D, CustomWidth = true },
            new Column { Min = 3U, Max = 3U, Width = 16D, CustomWidth = true }));

        worksheet.AppendChild(sheetData);

        if (entries.Count > 0)
        {
            worksheet.AppendChild(new AutoFilter
            {
                Reference = $"A{HeaderRow}:C{lastDataRow}"
            });
        }

        var mergeCells = new MergeCells();
        mergeCells.AppendChild(new MergeCell { Reference = $"A{TitleRow}:C{TitleRow}" });
        mergeCells.AppendChild(new MergeCell { Reference = $"A{TotalsRow}:C{TotalsRow}" });

        if (!string.IsNullOrWhiteSpace(options.DeveloperName))
        {
            mergeCells.AppendChild(new MergeCell { Reference = $"A{creditRow}:C{creditRow}" });
        }

        mergeCells.Count = (uint)mergeCells.ChildElements.Count;
        worksheet.AppendChild(mergeCells);

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
        double bodySize = options.FontSize;
        double titleSize = options.FontSize + 2;

        var fonts = new Fonts(
            // 0: body
            new Font(
                new FontSize { Val = bodySize },
                new FontName { Val = options.FontFamily }),
            // 1: bold
            new Font(
                new Bold(),
                new FontSize { Val = bodySize },
                new FontName { Val = options.FontFamily }),
            // 2: title
            new Font(
                new Bold(),
                new FontSize { Val = titleSize },
                new FontName { Val = options.FontFamily }))
        {
            Count = 3U
        };

        var fills = new Fills(
            // Excel reserves the first two fills; changing them corrupts the file.
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            // 2: header shading
            new Fill(new PatternFill(
                new ForegroundColor { Rgb = "FFD9D9D9" },
                new BackgroundColor { Indexed = 64U })
            { PatternType = PatternValues.Solid }))
        {
            Count = 3U
        };

        var borders = new Borders(
            // 0: none
            new Border(
                new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()),
            // 1: thin box
            new Border(
                new LeftBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new RightBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new TopBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new BottomBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new DiagonalBorder()))
        {
            Count = 2U
        };

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat { NumberFormatId = 0U, FontId = 0U, FillId = 0U, BorderId = 0U })
        {
            Count = 1U
        };

        var cellFormats = new CellFormats(
            // 0: default
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U },
            // 1: title
            new CellFormat
            {
                FontId = 2U,
                FillId = 0U,
                BorderId = 0U,
                ApplyFont = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
            },
            // 2: info line
            new CellFormat
            {
                FontId = 0U,
                FillId = 0U,
                BorderId = 0U,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
            },
            // 3: header
            new CellFormat
            {
                FontId = 1U,
                FillId = 2U,
                BorderId = 1U,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true,
                ApplyAlignment = true,
                Alignment = new Alignment
                {
                    Horizontal = HorizontalAlignmentValues.Center,
                    Vertical = VerticalAlignmentValues.Center,
                    WrapText = true
                }
            },
            // 4: file-name cell
            new CellFormat
            {
                FontId = 0U,
                FillId = 0U,
                BorderId = 1U,
                ApplyBorder = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center }
            },
            // 5: numeric cell
            new CellFormat
            {
                FontId = 0U,
                FillId = 0U,
                BorderId = 1U,
                ApplyBorder = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
            },
            // 6: totals label
            new CellFormat
            {
                FontId = 1U,
                FillId = 2U,
                BorderId = 1U,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
            },
            // 7: totals value
            new CellFormat
            {
                FontId = 1U,
                FillId = 2U,
                BorderId = 1U,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
            })
        {
            Count = 8U
        };

        var cellStyles = new CellStyles(
            new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
        {
            Count = 1U
        };

        return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
