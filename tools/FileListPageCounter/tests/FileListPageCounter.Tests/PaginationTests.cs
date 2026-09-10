using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// Choosing how many rows go on a page, and the promise that the printed document actually
/// obeys the number the user picked.
/// </summary>
public class PaginationTests
{
    private static List<ReportRow> Rows(int count)
    {
        var rows = new List<ReportRow>(count);
        for (int i = 1; i <= count; i++) rows.Add(new ReportRow(i, $"file-{i}", 1));
        return rows;
    }

    [Fact]
    public void The_offered_row_counts_are_the_ones_asked_for()
    {
        Assert.Equal(
            new[] { 10, 15, 20, 25, 30, 40, 50, 75, 100 },
            ReportOptions.SuggestedRowsPerPage);
    }

    [Fact]
    public void Rows_per_page_is_free_rather_than_fixed_to_the_list()
    {
        // A number outside the suggested list is accepted as it stands.
        Assert.Equal(37, new ReportOptions { RowsPerPage = 37 }.RowsPerPage);
        Assert.Equal(500, new ReportOptions { RowsPerPage = 500 }.RowsPerPage);

        // Zero and below mean "as many as fit".
        Assert.Equal(0, new ReportOptions().RowsPerPage);
        Assert.Equal(0, new ReportOptions { RowsPerPage = 0 }.RowsPerPage);
        Assert.Equal(0, new ReportOptions { RowsPerPage = -4 }.RowsPerPage);
    }

    [Fact]
    public void Pagination_puts_exactly_the_requested_number_on_each_page()
    {
        IReadOnlyList<ReportRow?[]> arranged = ReportLayout.Arrange(Rows(47), blocks: 1);
        IReadOnlyList<IReadOnlyList<ReportRow?[]>> pages = ReportLayout.Paginate(arranged, rowsPerPage: 10);

        Assert.Equal(5, pages.Count);
        Assert.Equal(10, pages[0].Count);
        Assert.Equal(10, pages[3].Count);
        Assert.Equal(7, pages[4].Count);   // the remainder
    }

    [Fact]
    public void The_first_page_carries_fewer_rows_because_the_title_sits_above_them()
    {
        IReadOnlyList<ReportRow?[]> arranged = ReportLayout.Arrange(Rows(60), blocks: 1);

        int firstPageRows = ReportLayout.FirstPageRows(fontSize: 20, rowsPerPage: 20);
        Assert.True(firstPageRows < 20, "the title block has to take its share");

        IReadOnlyList<IReadOnlyList<ReportRow?[]>> pages =
            ReportLayout.Paginate(arranged, rowsPerPage: 20, firstPageRows: firstPageRows);

        Assert.Equal(firstPageRows, pages[0].Count);
        Assert.Equal(20, pages[1].Count);
    }

    [Fact]
    public void No_row_is_lost_or_repeated_by_pagination()
    {
        IReadOnlyList<ReportRow?[]> arranged = ReportLayout.Arrange(Rows(133), blocks: 2);
        IReadOnlyList<IReadOnlyList<ReportRow?[]>> pages = ReportLayout.Paginate(arranged, rowsPerPage: 12, firstPageRows: 7);

        string[] names = pages
            .SelectMany(page => page)
            .SelectMany(line => line)
            .Where(row => row is not null)
            .Select(row => row!.Name)
            .ToArray();

        Assert.Equal(133, names.Length);
        Assert.Equal(133, names.Distinct().Count());
    }

    [Fact]
    public void An_unpinned_row_count_leaves_the_table_in_one_run()
    {
        IReadOnlyList<ReportRow?[]> arranged = ReportLayout.Arrange(Rows(200), blocks: 1);
        Assert.Single(ReportLayout.Paginate(arranged, rowsPerPage: 0));
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(100, 25)]
    [InlineData(1000, 50)]
    public void The_page_count_is_exact_once_the_rows_per_page_are_pinned(int entries, int rowsPerPage)
    {
        int pages = ReportLayout.EstimatePages(entries, fontSize: 20, blocks: 1, rowsPerPage: rowsPerPage);

        int firstPage = ReportLayout.FirstPageRows(20, rowsPerPage);
        int expected = 1 + (int)Math.Ceiling((entries - firstPage) / (double)rowsPerPage);

        Assert.Equal(expected, pages);
    }

    [Fact]
    public async Task Word_breaks_the_pages_where_the_user_asked_it_to()
    {
        using var output = new TempFolder();
        string path = output.File("paged.docx");

        WordReportBuilder.Build(path, Rows(45), new ReportOptions { RowsPerPage = 10 });

        using var document = WordprocessingDocument.Open(path, false);
        Body body = document.MainDocumentPart!.Document!.Body!;

        Table[] tables = body.Elements<Table>().ToArray();

        // The figure band plus one table per page of rows.
        int firstPage = ReportLayout.FirstPageRows(ReportOptions.DefaultFontSize, 10);
        int expectedPages = 1 + (int)Math.Ceiling((45 - firstPage) / 10d);
        Assert.Equal(expectedPages + 1, tables.Length);

        // A hard page break sits between each pair of tables.
        Assert.Equal(expectedPages - 1, body.Descendants<Break>().Count(b => b.Type is not null && b.Type.Value == BreakValues.Page));

        // Every one of those tables repeats the header, so no page prints without one.
        foreach (Table table in tables.Skip(1))
        {
            Assert.NotNull(table.Elements<TableRow>().First().TableRowProperties!.GetFirstChild<TableHeader>());
        }

        await Task.CompletedTask;
    }

    [Fact]
    public void Word_keeps_one_table_when_the_row_count_is_left_automatic()
    {
        using var output = new TempFolder();
        string path = output.File("auto.docx");

        WordReportBuilder.Build(path, Rows(200), new ReportOptions());

        using var document = WordprocessingDocument.Open(path, false);
        Body body = document.MainDocumentPart!.Document!.Body!;

        // The figure band and the data table, and no forced breaks.
        Assert.Equal(2, body.Elements<Table>().Count());
        Assert.DoesNotContain(body.Descendants<Break>(), b => b.Type is not null && b.Type.Value == BreakValues.Page);
    }
}
