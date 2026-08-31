using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Core.Scanning;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>The Excel report: a genuine XLSX with the same data, totals and credit as the Word one.</summary>
public class ExcelReportTests
{
    private sealed class ReportFixture : IDisposable
    {
        public required TempFolder Source { get; init; }

        public required TempFolder Output { get; init; }

        public required string Path { get; init; }

        public required ScanResult Result { get; init; }

        public void Dispose()
        {
            Output.Dispose();
            Source.Dispose();
        }
    }

    private static async Task<ReportFixture> BuildReportAsync(ReportOptions? options = null)
    {
        var source = new TempFolder();
        var output = new TempFolder();

        TestPdfFactory.Write(source.File("10001.pdf"), 4);
        TestPdfFactory.Write(source.File("10002.pdf"), 7);
        TestImageFactory.WriteJpeg(source.File("وثيقة 10003.jpg"));
        TestPdfFactory.WriteCorrupt(source.File("10004.pdf"));

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());

        string path = output.File("report.xlsx");
        ExcelReportBuilder.Build(path, result.Entries, options ?? new ReportOptions());

        return new ReportFixture { Source = source, Output = output, Path = path, Result = result };
    }

    private static string CellText(Cell cell)
    {
        if (cell.DataType is not null && cell.DataType.Value == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.Text ?? string.Empty;
        }

        return cell.CellValue?.Text ?? string.Empty;
    }

    private static Row RowAt(SheetData data, uint index) =>
        data.Elements<Row>().Single(r => r.RowIndex is not null && r.RowIndex.Value == index);

    [Fact]
    public async Task The_output_is_a_real_open_xml_workbook_not_a_renamed_csv()
    {
        using ReportFixture fixture = await BuildReportAsync();

        using (var zip = ZipFile.OpenRead(fixture.Path))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "[Content_Types].xml");
            Assert.Contains(zip.Entries, e => e.FullName == "xl/workbook.xml");
            Assert.Contains(zip.Entries, e => e.FullName == "xl/styles.xml");
            Assert.Contains(zip.Entries, e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
        }

        using var document = SpreadsheetDocument.Open(fixture.Path, false);
        Assert.NotNull(document.WorkbookPart?.Workbook);
        Assert.Single(document.WorkbookPart!.Workbook.Descendants<Sheet>());
    }

    [Fact]
    public async Task The_sheet_is_right_to_left_with_a_frozen_header()
    {
        using ReportFixture fixture = await BuildReportAsync();
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        Worksheet worksheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet;

        SheetView view = worksheet.Descendants<SheetView>().Single();
        Assert.True(view.RightToLeft!.Value);

        Pane pane = view.Descendants<Pane>().Single();
        Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);
        Assert.Equal(4D, pane.VerticalSplit!.Value); // freeze everything above the data
    }

    [Fact]
    public async Task The_header_carries_the_three_required_columns()
    {
        using ReportFixture fixture = await BuildReportAsync();
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        string[] header = RowAt(data, 4).Elements<Cell>().Select(CellText).ToArray();
        Assert.Equal(new[] { "م", "اسم الملف", "عدد الصفحات" }, header);
    }

    [Fact]
    public async Task Each_file_becomes_a_row_with_its_name_and_page_count()
    {
        using ReportFixture fixture = await BuildReportAsync();
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        Assert.Equal(new[] { "1", "10001", "4" }, RowAt(data, 5).Elements<Cell>().Select(CellText).ToArray());
        Assert.Equal(new[] { "2", "10002", "7" }, RowAt(data, 6).Elements<Cell>().Select(CellText).ToArray());

        // The damaged file is listed rather than dropped, exactly as in the Word report.
        Assert.Equal(new[] { "3", "10004", Strings.Unknown }, RowAt(data, 7).Elements<Cell>().Select(CellText).ToArray());
        Assert.Equal(new[] { "4", "وثيقة 10003", "1" }, RowAt(data, 8).Elements<Cell>().Select(CellText).ToArray());
    }

    [Fact]
    public async Task Page_counts_are_stored_as_numbers_so_Excel_can_sum_them()
    {
        using ReportFixture fixture = await BuildReportAsync();
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        Cell knownCount = RowAt(data, 5).Elements<Cell>().Single(c => c.CellReference == "C5");
        Assert.Equal(CellValues.Number, knownCount.DataType!.Value);

        // Only the unknown one falls back to text.
        Cell unknownCount = RowAt(data, 7).Elements<Cell>().Single(c => c.CellReference == "C7");
        Assert.Equal(CellValues.InlineString, unknownCount.DataType!.Value);
    }

    [Fact]
    public async Task The_totals_and_the_grand_total_row_are_correct()
    {
        using ReportFixture fixture = await BuildReportAsync();
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        string totalsLine = CellText(RowAt(data, 2).Elements<Cell>().First());
        Assert.Contains($"{Strings.TotalFiles}: 4", totalsLine);
        Assert.Contains($"{Strings.TotalPages}: 12", totalsLine);   // 4 + 7 + 1, the damaged file counts nothing
        Assert.Contains($"{Strings.UnknownFiles}: 1", totalsLine);

        Row grandTotal = RowAt(data, 9);
        Assert.Equal(Strings.GrandTotal, CellText(grandTotal.Elements<Cell>().Single(c => c.CellReference == "B9")));
        Assert.Equal("12", CellText(grandTotal.Elements<Cell>().Single(c => c.CellReference == "C9")));
    }

    [Fact]
    public async Task The_header_row_repeats_on_every_printed_page_and_the_page_is_A4()
    {
        using ReportFixture fixture = await BuildReportAsync();
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        DefinedName printTitles = document.WorkbookPart!.Workbook.Descendants<DefinedName>()
            .Single(n => n.Name == "_xlnm.Print_Titles");
        Assert.Contains("$4:$4", printTitles.Text);

        PageSetup setup = document.WorkbookPart.WorksheetParts.Single().Worksheet.Descendants<PageSetup>().Single();
        Assert.Equal(9U, setup.PaperSize!.Value);                        // A4
        Assert.Equal(OrientationValues.Portrait, setup.Orientation!.Value);
    }

    [Fact]
    public async Task The_title_and_the_developer_credit_appear_in_the_sheet()
    {
        using ReportFixture fixture = await BuildReportAsync(new ReportOptions { Title = "أرشيف 2026" });
        using var document = SpreadsheetDocument.Open(fixture.Path, false);

        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        Assert.Equal("أرشيف 2026", CellText(RowAt(data, 1).Elements<Cell>().First()));

        string allText = string.Join("\n", data.Descendants<Cell>().Select(CellText));
        Assert.Contains("Ibrahim Masry Ibrahim", allText, StringComparison.Ordinal);

        Assert.Equal("أرشيف 2026", document.PackageProperties.Title);
        Assert.Equal("Ibrahim Masry Ibrahim", document.PackageProperties.Creator);
    }

    [Fact]
    public void An_empty_result_still_produces_a_readable_workbook()
    {
        using var output = new TempFolder();

        string path = output.File("empty.xlsx");
        ExcelReportBuilder.Build(path, Array.Empty<FileEntry>(), new ReportOptions());

        using var document = SpreadsheetDocument.Open(path, false);
        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        Assert.Equal(Strings.ReportTitle, CellText(RowAt(data, 1).Elements<Cell>().First()));
        Assert.Equal(3, RowAt(data, 4).Elements<Cell>().Count()); // the header is still there
    }

    [Fact]
    public async Task A_large_workbook_holds_every_row()
    {
        const int count = 1000;

        using var source = new TempFolder();
        for (int i = 1; i <= count; i++)
        {
            TestImageFactory.WritePng(source.File($"page-{i}.png"));
        }

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());

        using var output = new TempFolder();
        string path = output.File("large.xlsx");
        ExcelReportBuilder.Build(path, result.Entries, new ReportOptions());

        using var document = SpreadsheetDocument.Open(path, false);
        SheetData data = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<SheetData>().Single();

        // 1 title + 1 totals + 1 header + 1000 data + 1 grand total + 1 credit
        Assert.Equal(count, data.Elements<Row>().Count(r => r.RowIndex!.Value >= 5 && r.RowIndex.Value <= 4 + count));
        Assert.Equal("1000", CellText(RowAt(data, (uint)(5 + count)).Elements<Cell>().Single(c => c.CellReference == "C" + (5 + count))));
    }
}
