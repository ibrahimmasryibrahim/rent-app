using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// The page-layout engine: how the list is spread across the width of the page, and the page
/// count the export dialog promises before the user commits.
/// </summary>
public class ReportLayoutTests
{
    private static IReadOnlyList<FileEntry> Entries(int count)
    {
        var list = new List<FileEntry>(count);

        for (int i = 1; i <= count; i++)
        {
            var entry = new FileEntry
            {
                DiscoveryOrder = i - 1,
                FullPath = $@"C:\archive\{i}.pdf",
                FileName = $"{i}.pdf",
                DisplayName = i.ToString(),
                Extension = ".pdf"
            };

            entry.Index = i;
            entry.PageCount = 1;
            list.Add(entry);
        }

        return list;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]     // clamped to the maximum
    [InlineData(-5, 1)]    // clamped to the minimum
    public void The_block_count_is_clamped_to_one_two_or_three(int given, int expected)
    {
        Assert.Equal(expected, ReportLayout.NormalizeBlocks(given));
        Assert.Equal(expected, new ReportOptions { ColumnBlocks = given }.ColumnBlocks);
    }

    [Fact]
    public void A_single_block_is_the_default()
    {
        Assert.Equal(1, new ReportOptions().ColumnBlocks);
    }

    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(100, 2, 50)]
    [InlineData(100, 3, 34)]   // 34 rows: the last one is partly empty
    [InlineData(7, 3, 3)]
    [InlineData(0, 3, 0)]
    public void Rows_are_the_entries_divided_over_the_blocks(int entries, int blocks, int expectedRows)
    {
        Assert.Equal(expectedRows, ReportLayout.TableRowCount(entries, blocks));
    }

    [Fact]
    public void Entries_run_down_each_block_before_moving_to_the_next()
    {
        // Six entries over two blocks: 1,2,3 down the first block and 4,5,6 down the second,
        // so the numbering still reads in order down the page.
        IReadOnlyList<FileEntry?[]> rows = ReportLayout.Arrange(Entries(6), blocks: 2);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "1", "4" }, rows[0].Select(e => e!.DisplayName).ToArray());
        Assert.Equal(new[] { "2", "5" }, rows[1].Select(e => e!.DisplayName).ToArray());
        Assert.Equal(new[] { "3", "6" }, rows[2].Select(e => e!.DisplayName).ToArray());
    }

    [Fact]
    public void A_list_that_does_not_divide_evenly_leaves_empty_cells_at_the_end()
    {
        IReadOnlyList<FileEntry?[]> rows = ReportLayout.Arrange(Entries(5), blocks: 2);

        Assert.Equal(3, rows.Count);
        Assert.Equal("1", rows[0][0]!.DisplayName);
        Assert.Equal("4", rows[0][1]!.DisplayName);
        Assert.Equal("3", rows[2][0]!.DisplayName);
        Assert.Null(rows[2][1]);   // the grid stays rectangular
    }

    [Fact]
    public void Every_entry_appears_exactly_once_whatever_the_block_count()
    {
        foreach (int blocks in new[] { 1, 2, 3 })
        {
            string[] laid = ReportLayout.Arrange(Entries(97), blocks)
                .SelectMany(row => row)
                .Where(e => e is not null)
                .Select(e => e!.DisplayName)
                .ToArray();

            Assert.Equal(97, laid.Length);
            Assert.Equal(97, laid.Distinct().Count());
        }
    }

    [Fact]
    public void More_blocks_never_need_more_pages()
    {
        int one = ReportLayout.EstimatePages(500, fontSize: 20, blocks: 1);
        int two = ReportLayout.EstimatePages(500, fontSize: 20, blocks: 2);
        int three = ReportLayout.EstimatePages(500, fontSize: 20, blocks: 3);

        Assert.True(two < one, $"two blocks ({two}) should beat one ({one})");
        Assert.True(three < two, $"three blocks ({three}) should beat two ({two})");
    }

    [Fact]
    public void A_larger_font_never_needs_fewer_pages()
    {
        int small = ReportLayout.EstimatePages(500, fontSize: 16, blocks: 1);
        int large = ReportLayout.EstimatePages(500, fontSize: 24, blocks: 1);

        Assert.True(large >= small);
    }

    [Fact]
    public void A_short_list_fits_on_one_page()
    {
        Assert.Equal(1, ReportLayout.EstimatePages(0, 20, 1));
        Assert.Equal(1, ReportLayout.EstimatePages(5, 20, 1));
    }

    [Fact]
    public void The_page_estimate_grows_in_step_with_the_list()
    {
        int previous = 0;

        foreach (int count in new[] { 10, 50, 200, 1000, 5000 })
        {
            int pages = ReportLayout.EstimatePages(count, fontSize: 20, blocks: 1);
            Assert.True(pages >= previous, "the estimate must never go backwards as the list grows");
            previous = pages;
        }

        // A thousand rows at 20 pt on A4 lands in the twenties, not the hundreds or the single digits.
        int thousand = ReportLayout.EstimatePages(1000, fontSize: 20, blocks: 1);
        Assert.InRange(thousand, 25, 60);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Column_widths_always_fill_the_page_without_overflowing(int blocks)
    {
        const int usable = 9638;

        (int index, int name, int count) = ReportLayout.BlockColumnWidths(usable, blocks);

        Assert.True(index > 0 && name > 0 && count > 0, "no column may collapse");
        Assert.True((index + name + count) * blocks <= usable, "the blocks must fit the page width");
        Assert.True(name > index, "the file name needs the widest column");
    }
}
