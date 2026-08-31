using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Decides how the file list is laid out on the printed page, and tells the user in advance how
/// many pages that will take.
///
/// A file list is a narrow thing — a number, a name, a count — so a single table down the middle
/// of an A4 page wastes most of the paper. Repeating the three columns two or three times across
/// the page turns a thirty-page print into a ten-page one. Items run down the first block, then
/// down the second, the way a newspaper column reads, so the numbering stays sequential to the eye.
/// </summary>
public static class ReportLayout
{
    public const int MinBlocks = 1;
    public const int MaxBlocks = 3;

    /// <summary>Usable A4 height in twips: page height less the top and bottom margins.</summary>
    private const int UsableHeightTwips = 16838 - 1247 - 1134; // 14457

    /// <summary>Title, date line, figure band and the spacing around them, on the first page only.</summary>
    private const int TitleBlockTwips = 2600;

    /// <summary>The closing summary block that follows the table.</summary>
    private const int SummaryBlockTwips = 1900;

    public static int NormalizeBlocks(int blocks) => Math.Clamp(blocks, MinBlocks, MaxBlocks);

    /// <summary>
    /// Height of one table row: the text line at Word's default 1.15 spacing, plus the cell
    /// padding above and below it.
    /// </summary>
    public static int EstimateRowHeightTwips(int fontSize) =>
        (int)Math.Round(fontSize * 20 * 1.15) + 140;

    /// <summary>How many table rows the entries occupy once spread over the blocks.</summary>
    public static int TableRowCount(int entryCount, int blocks)
    {
        blocks = NormalizeBlocks(blocks);
        return entryCount <= 0 ? 0 : (entryCount + blocks - 1) / blocks;
    }

    /// <summary>
    /// A close estimate of the printed page count — close enough to choose a layout by, but it is
    /// still an estimate: Word decides the final pagination from the actual font metrics.
    /// </summary>
    public static int EstimatePages(int entryCount, int fontSize, int blocks)
    {
        int rows = TableRowCount(entryCount, blocks);
        int rowHeight = EstimateRowHeightTwips(fontSize);
        int headerHeight = rowHeight;

        int firstPageBody = UsableHeightTwips - TitleBlockTwips - headerHeight;
        int otherPageBody = UsableHeightTwips - headerHeight;

        int rowsOnFirstPage = Math.Max(0, firstPageBody / rowHeight);
        int rowsPerLaterPage = Math.Max(1, otherPageBody / rowHeight);

        int pages;
        int rowsOnLastPage;

        if (rows <= rowsOnFirstPage)
        {
            pages = 1;
            rowsOnLastPage = rows;
        }
        else
        {
            int remaining = rows - rowsOnFirstPage;
            int laterPages = (remaining + rowsPerLaterPage - 1) / rowsPerLaterPage;
            pages = 1 + laterPages;
            rowsOnLastPage = remaining - ((laterPages - 1) * rowsPerLaterPage);
        }

        // The summary needs room under the last row, or it pushes onto a page of its own.
        int usedOnLastPage = (pages == 1 ? TitleBlockTwips : 0) + headerHeight + (rowsOnLastPage * rowHeight);
        if (usedOnLastPage + SummaryBlockTwips > UsableHeightTwips)
        {
            pages++;
        }

        return Math.Max(1, pages);
    }

    /// <summary>
    /// Column widths for one block, given the total usable width. The index and count columns keep
    /// a sensible minimum; the name column takes whatever is left, because that is the only column
    /// whose content varies in length.
    /// </summary>
    public static (int Index, int Name, int Count) BlockColumnWidths(int usableWidth, int blocks)
    {
        blocks = NormalizeBlocks(blocks);
        int blockWidth = usableWidth / blocks;

        (int index, int count) = blocks switch
        {
            1 => (850, 2000),
            2 => (620, 1180),
            _ => (520, 940)
        };

        return (index, blockWidth - index - count, count);
    }

    /// <summary>
    /// Arranges the entries into table rows, filling each block top to bottom before moving to the
    /// next — so block one holds items 1..n, block two the next n, and the numbers still read in
    /// order down the page. Cells past the end of the list come back null.
    /// </summary>
    public static IReadOnlyList<FileEntry?[]> Arrange(IReadOnlyList<FileEntry> entries, int blocks)
    {
        ArgumentNullException.ThrowIfNull(entries);

        blocks = NormalizeBlocks(blocks);
        int rows = TableRowCount(entries.Count, blocks);

        var table = new List<FileEntry?[]>(rows);

        for (int row = 0; row < rows; row++)
        {
            var cells = new FileEntry?[blocks];

            for (int block = 0; block < blocks; block++)
            {
                int position = (block * rows) + row;
                cells[block] = position < entries.Count ? entries[position] : null;
            }

            table.Add(cells);
        }

        return table;
    }
}
