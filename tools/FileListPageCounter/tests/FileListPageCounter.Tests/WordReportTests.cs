using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Core.Scanning;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>Requirements 10 to 15: a genuine, print-ready DOCX with the exact layout asked for.</summary>
public class WordReportTests
{
    /// <summary>Source files, the generated report and the scan behind it, all cleaned up together.</summary>
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

    private static async Task<ReportFixture> BuildReportAsync(int fileCount = 4, ReportOptions? options = null)
    {
        var source = new TempFolder();
        var output = new TempFolder();

        TestPdfFactory.Write(source.File("10001.pdf"), 4);
        TestPdfFactory.Write(source.File("10002.pdf"), 7);
        TestPdfFactory.Write(source.File("10003.pdf"), 2);
        TestImageFactory.WriteJpeg(source.File("وثيقة 10004.jpg"));

        for (int i = 5; i <= fileCount; i++)
        {
            TestPdfFactory.Write(source.File($"1{i:D4}.pdf"), 1);
        }

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());

        // The report is deliberately written outside the source folder.
        string path = output.File("report.docx");
        WordReportBuilder.Build(path, result.Entries, options ?? new ReportOptions());

        return new ReportFixture { Source = source, Output = output, Path = path, Result = result };
    }

    [Fact]
    public async Task The_output_is_a_real_open_xml_package_not_renamed_html()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var zip = ZipFile.OpenRead(path);

        Assert.Contains(zip.Entries, e => e.FullName == "[Content_Types].xml");
        Assert.Contains(zip.Entries, e => e.FullName == "word/document.xml");
        Assert.Contains(zip.Entries, e => e.FullName == "word/styles.xml");

        using var document = WordprocessingDocument.Open(path, false);
        Assert.NotNull(document.MainDocumentPart?.Document?.Body);
    }

    [Fact]
    public async Task The_page_is_A4_portrait_with_margins()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);
        Body body = document.MainDocumentPart!.Document!.Body!;

        PageSize size = body.Descendants<PageSize>().Single();
        Assert.Equal(11906U, size.Width!.Value);   // A4 width in twips
        Assert.Equal(16838U, size.Height!.Value);  // A4 height in twips
        Assert.Equal(PageOrientationValues.Portrait, size.Orient!.Value);

        PageMargin margin = body.Descendants<PageMargin>().Single();
        Assert.Equal(1247, margin.Top!.Value);   // extra room for the running header
        Assert.Equal(1134U, margin.Left!.Value);
        Assert.Equal(1134U, margin.Right!.Value);
    }

    [Fact]
    public async Task The_document_is_right_to_left()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);
        Body body = document.MainDocumentPart!.Document!.Body!;

        // Section, paragraphs, runs and the table itself all carry RTL.
        Assert.NotNull(body.Descendants<SectionProperties>().Single().GetFirstChild<BiDi>());
        Assert.NotNull(body.Descendants<Table>().Last().GetFirstChild<TableProperties>()!.GetFirstChild<BiDiVisual>());
        Assert.All(body.Descendants<Paragraph>(), p => Assert.NotNull(p.ParagraphProperties?.GetFirstChild<BiDi>()));
        Assert.NotEmpty(body.Descendants<RightToLeftText>());
    }

    [Fact]
    public async Task The_default_font_is_Arial_at_size_twenty()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);

        RunPropertiesBaseStyle defaults = document.MainDocumentPart!.StyleDefinitionsPart!
            .Styles!.DocDefaults!.RunPropertiesDefault!.RunPropertiesBaseStyle!;

        Assert.Equal("Arial", defaults.RunFonts!.Ascii!.Value);
        Assert.Equal("Arial", defaults.RunFonts.ComplexScript!.Value);

        // Word stores sizes in half-points: 20 pt = 40.
        Assert.Equal("40", defaults.FontSize!.Val!.Value);
        Assert.Equal("40", defaults.FontSizeComplexScript!.Val!.Value);

        // Every cell of the data table uses exactly the size the user chose.
        Table table = document.MainDocumentPart.Document!.Body!.Descendants<Table>().Last();
        Assert.All(table.Descendants<FontSize>(), fs => Assert.Equal("40", fs.Val!.Value));
    }

    [Theory]
    [InlineData(16, "32")]
    [InlineData(18, "36")]
    [InlineData(22, "44")]
    [InlineData(24, "48")]
    public async Task The_font_size_is_configurable(int points, string expectedHalfPoints)
    {
        using ReportFixture fixture = await BuildReportAsync(options: new ReportOptions { FontSize = points });
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);

        RunPropertiesBaseStyle defaults = document.MainDocumentPart!.StyleDefinitionsPart!
            .Styles!.DocDefaults!.RunPropertiesDefault!.RunPropertiesBaseStyle!;

        Assert.Equal(expectedHalfPoints, defaults.FontSize!.Val!.Value);
    }

    [Fact]
    public void An_invalid_font_size_falls_back_to_twenty()
    {
        Assert.Equal(20, new ReportOptions { FontSize = 13 }.FontSize);
        Assert.Equal(24, new ReportOptions { FontSize = 24 }.FontSize);
        Assert.Equal(20, new ReportOptions().FontSize);
    }

    [Fact]
    public async Task The_table_has_the_three_required_columns_in_order()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        ScanResult result = fixture.Result;
        using var document = WordprocessingDocument.Open(path, false);
        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();

        TableRow header = table.Elements<TableRow>().First();
        string[] headings = header.Elements<TableCell>().Select(c => c.InnerText).ToArray();

        Assert.Equal(new[] { "م", "اسم الملف", "عدد الصفحات" }, headings);

        // One header row plus one row per file.
        Assert.Equal(result.TotalFiles + 1, table.Elements<TableRow>().Count());
    }

    [Fact]
    public async Task The_header_row_repeats_on_every_page_and_rows_never_split()
    {
        using ReportFixture fixture = await BuildReportAsync(fileCount: 120);
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);
        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();

        TableRow[] rows = table.Elements<TableRow>().ToArray();

        Assert.NotNull(rows[0].TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.All(rows, r => Assert.NotNull(r.TableRowProperties!.GetFirstChild<CantSplit>()));

        // Only the first row is a repeating header.
        Assert.Single(table.Descendants<TableHeader>());
    }

    [Fact]
    public async Task The_rows_carry_the_file_name_without_its_extension_and_the_page_count()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);
        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();

        string[][] rows = table.Elements<TableRow>()
            .Skip(1)
            .Select(r => r.Elements<TableCell>().Select(c => c.InnerText).ToArray())
            .ToArray();

        Assert.Equal(new[] { "1", "10001", "4" }, rows[0]);
        Assert.Equal(new[] { "2", "10002", "7" }, rows[1]);
        Assert.Equal(new[] { "3", "10003", "2" }, rows[2]);
        Assert.Equal(new[] { "4", "وثيقة 10004", "1" }, rows[3]);
    }

    [Fact]
    public async Task The_title_and_the_totals_appear_above_the_table()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);
        Body body = document.MainDocumentPart!.Document!.Body!;

        Paragraph title = body.Elements<Paragraph>().First();
        Assert.Equal("قائمة الملفات وعدد الصفحات", title.InnerText);
        Assert.Equal(JustificationValues.Center, title.ParagraphProperties!.Justification!.Val!.Value);
        Assert.NotNull(title.Descendants<Bold>().FirstOrDefault());

        // The headline figures sit in their own band, above the data table.
        Table figureBand = body.Descendants<Table>().First();
        string[] cells = figureBand.Descendants<TableCell>().Select(c => c.InnerText).ToArray();

        Assert.Equal(3, cells.Length);
        Assert.Contains("4", cells[0], StringComparison.Ordinal);
        Assert.Contains("إجمالي عدد الملفات", cells[0], StringComparison.Ordinal);
        Assert.Contains("14", cells[1], StringComparison.Ordinal);
        Assert.Contains("إجمالي عدد الصفحات", cells[1], StringComparison.Ordinal);
        Assert.Contains("إجمالي عدد الملفات: 4", body.InnerText);      // and again in the closing summary
        Assert.Contains("إجمالي عدد الصفحات: 14", body.InnerText);
    }

    [Fact]
    public async Task A_summary_closes_the_document()
    {
        var source = new TempFolder();
        using (source)
        {
            TestPdfFactory.Write(source.File("ok-1.pdf"), 5);
            TestPdfFactory.Write(source.File("ok-2.pdf"), 6);
            TestPdfFactory.WriteCorrupt(source.File("bad-1.pdf"));

            ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());

            using var output = new TempFolder();
            string path = output.File("summary.docx");
            WordReportBuilder.Build(path, result.Entries, new ReportOptions());

            using var document = WordprocessingDocument.Open(path, false);
            string text = document.MainDocumentPart!.Document!.Body!.InnerText;

            Assert.Contains("الملخص", text);
            Assert.Contains("إجمالي عدد الملفات: 3", text);
            Assert.Contains("إجمالي عدد الصفحات: 11", text);
            Assert.Contains("عدد الملفات التي تعذر تحديد صفحاتها: 1", text);
            Assert.Contains("غير معروف", text);
        }
    }

    [Fact]
    public async Task A_page_number_footer_is_added_for_printing()
    {
        using ReportFixture fixture = await BuildReportAsync();
        string path = fixture.Path;
        using var document = WordprocessingDocument.Open(path, false);

        FooterPart footer = Assert.Single(document.MainDocumentPart!.FooterParts);
        Assert.Contains("صفحة", footer.Footer!.InnerText);
        Assert.NotNull(document.MainDocumentPart.Document!.Body!
            .Descendants<SectionProperties>().Single().GetFirstChild<FooterReference>());
    }

    [Fact]
    public async Task A_large_report_is_produced_with_every_row()
    {
        const int count = 1000;

        using var source = new TempFolder();
        for (int i = 1; i <= count; i++)
        {
            TestImageFactory.WritePng(source.File($"page-{i}.png"));
        }

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());

        using var output = new TempFolder();
        string path = output.File("large.docx");
        WordReportBuilder.Build(path, result.Entries, new ReportOptions());

        using var document = WordprocessingDocument.Open(path, false);
        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();

        Assert.Equal(count + 1, table.Elements<TableRow>().Count());
        Assert.Contains("إجمالي عدد الصفحات: 1,000", document.MainDocumentPart.Document.Body!.InnerText);
    }

    [Fact]
    public async Task The_title_can_be_the_folder_name_or_anything_the_user_types()
    {
        using ReportFixture fixture = await BuildReportAsync(options: new ReportOptions { Title = "أرشيف العقود 2026" });
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Paragraph title = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();

        Assert.Equal("أرشيف العقود 2026", title.InnerText);
        Assert.Equal(JustificationValues.Center, title.ParagraphProperties!.Justification!.Val!.Value);
        Assert.NotNull(title.Descendants<Bold>().FirstOrDefault());
    }

    [Fact]
    public async Task A_blank_title_falls_back_to_the_standard_heading()
    {
        using ReportFixture fixture = await BuildReportAsync(options: new ReportOptions { Title = "   " });
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Assert.Equal(
            "قائمة الملفات وعدد الصفحات",
            document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First().InnerText);
    }

    [Fact]
    public async Task No_person_is_named_as_the_preparer_of_the_report()
    {
        // The report is generated from a folder listing; nobody prepared or signed it, so it
        // must not carry anyone's name — not in the body, not in the footer, not in the metadata.
        using ReportFixture fixture = await BuildReportAsync();
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Assert.DoesNotContain("إعداد", document.MainDocumentPart!.Document!.Body!.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("Ibrahim", document.MainDocumentPart.Document.Body!.InnerText, StringComparison.OrdinalIgnoreCase);

        FooterPart footer = Assert.Single(document.MainDocumentPart.FooterParts);
        Assert.DoesNotContain("Ibrahim", footer.Footer!.InnerText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("إعداد", footer.Footer.InnerText, StringComparison.Ordinal);

        // The footer still does its own job.
        Assert.Contains("صفحة", footer.Footer.InnerText, StringComparison.Ordinal);

        // The author field names the tool, not a person.
        Assert.Equal("FILE LIST & PAGE COUNTER", document.PackageProperties.Creator);
    }

    [Fact]
    public async Task Two_blocks_halve_the_rows_and_triple_the_columns()
    {
        using ReportFixture fixture = await BuildReportAsync(fileCount: 12, options: new ReportOptions { ColumnBlocks = 2 });
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();
        TableRow[] rows = table.Elements<TableRow>().ToArray();

        // 12 entries over two blocks: a header plus six rows of six cells.
        Assert.Equal(7, rows.Length);
        Assert.Equal(6, rows[0].Elements<TableCell>().Count());
        Assert.Equal(6, table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());

        // The headings repeat once per block.
        Assert.Equal(
            new[] { "م", "اسم الملف", "عدد الصفحات", "م", "اسم الملف", "عدد الصفحات" },
            rows[0].Elements<TableCell>().Select(c => c.InnerText).ToArray());
    }

    [Fact]
    public async Task Three_blocks_lay_the_numbers_out_down_each_block_in_turn()
    {
        using ReportFixture fixture = await BuildReportAsync(fileCount: 9, options: new ReportOptions { ColumnBlocks = 3 });
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();
        TableRow[] rows = table.Elements<TableRow>().Skip(1).ToArray();

        Assert.Equal(3, rows.Length);

        // Row one holds items 1, 4 and 7 — the top of each of the three blocks.
        string[] first = rows[0].Elements<TableCell>().Select(c => c.InnerText).ToArray();
        Assert.Equal("1", first[0]);
        Assert.Equal("4", first[3]);
        Assert.Equal("7", first[6]);
    }

    [Fact]
    public async Task An_uneven_list_still_produces_a_rectangular_table()
    {
        using ReportFixture fixture = await BuildReportAsync(fileCount: 7, options: new ReportOptions { ColumnBlocks = 3 });
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();

        // Every row carries nine cells even where the list has run out.
        Assert.All(table.Elements<TableRow>(), r => Assert.Equal(9, r.Elements<TableCell>().Count()));

        // Every file still appears exactly once.
        string text = table.InnerText;
        foreach (string name in new[] { "10001", "10002", "10003", "10005", "10006", "10007" })
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_header_still_repeats_and_rows_still_hold_together_with_several_blocks()
    {
        using ReportFixture fixture = await BuildReportAsync(fileCount: 200, options: new ReportOptions { ColumnBlocks = 2 });
        using var document = WordprocessingDocument.Open(fixture.Path, false);

        Table table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last();
        TableRow[] rows = table.Elements<TableRow>().ToArray();

        Assert.NotNull(rows[0].TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.Single(table.Descendants<TableHeader>());
        Assert.All(rows, r => Assert.NotNull(r.TableRowProperties!.GetFirstChild<CantSplit>()));
    }
}
