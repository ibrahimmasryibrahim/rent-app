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
    /// How many table rows fit on a page when the user has not pinned a number. Used to fill in
    /// "تلقائي" with a concrete figure for the preview.
    /// </summary>
    public static int RowsThatFitOnAPage(int fontSize, bool firstPage)
    {
        int rowHeight = EstimateRowHeightTwips(fontSize);
        int body = UsableHeightTwips - (firstPage ? TitleBlockTwips : 0) - rowHeight;
        return Math.Max(1, body / rowHeight);
    }

    /// <summary>
    /// How many table rows the title block displaces on the first page. The first page has to
    /// carry that many fewer rows, or the title pushes the last of them onto a page of their own.
    /// </summary>
    public static int TitleBlockRowEquivalent(int fontSize) =>
        (int)Math.Ceiling(TitleBlockTwips / (double)EstimateRowHeightTwips(fontSize));

    /// <summary>Rows allowed on the first page once the title block has taken its share.</summary>
    public static int FirstPageRows(int fontSize, int rowsPerPage) =>
        rowsPerPage <= 0 ? 0 : Math.Max(1, rowsPerPage - TitleBlockRowEquivalent(fontSize));

    /// <summary>
    /// The page count when each page carries exactly <paramref name="rowsPerPage"/> table rows.
    /// This is exact rather than estimated, because the report writer breaks the pages itself.
    /// </summary>
    public static int PagesAtFixedRowsPerPage(int entryCount, int blocks, int rowsPerPage, int fontSize)
    {
        if (rowsPerPage <= 0) throw new ArgumentOutOfRangeException(nameof(rowsPerPage));

        int rows = TableRowCount(entryCount, blocks);
        if (rows == 0) return 1;

        int firstPage = FirstPageRows(fontSize, rowsPerPage);
        if (rows <= firstPage) return 1;

        return 1 + (((rows - firstPage) + rowsPerPage - 1) / rowsPerPage);
    }

    /// <summary>
    /// A close estimate of the printed page count — close enough to choose a layout by, but it is
    /// still an estimate: Word decides the final pagination from the actual font metrics.
    /// </summary>
    public static int EstimatePages(int entryCount, int fontSize, int blocks, int rowsPerPage = 0)
    {
        if (rowsPerPage > 0)
        {
            return PagesAtFixedRowsPerPage(entryCount, blocks, rowsPerPage, fontSize);
        }

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
    public static IReadOnlyList<ReportRow?[]> Arrange(IReadOnlyList<ReportRow> rows, int blocks)
    {
        ArgumentNullException.ThrowIfNull(rows);

        blocks = NormalizeBlocks(blocks);
        int lines = TableRowCount(rows.Count, blocks);

        var table = new List<ReportRow?[]>(lines);

        for (int line = 0; line < lines; line++)
        {
            var cells = new ReportRow?[blocks];

            for (int block = 0; block < blocks; block++)
            {
                int position = (block * lines) + line;
                cells[block] = position < rows.Count ? rows[position] : null;
            }

            table.Add(cells);
        }

        return table;
    }

    /// <summary>
    /// Splits arranged rows into pages. With a pinned row count each page holds exactly that many
    /// rows; with zero the whole table is one run and the word processor breaks it where it likes.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ReportRow?[]>> Paginate(
        IReadOnlyList<ReportRow?[]> arranged,
        int rowsPerPage,
        int firstPageRows = 0)
    {
        ArgumentNullException.ThrowIfNull(arranged);

        if (rowsPerPage <= 0)
        {
            return new[] { arranged };
        }

        if (firstPageRows <= 0) firstPageRows = rowsPerPage;

        var pages = new List<IReadOnlyList<ReportRow?[]>>();
        int start = 0;
        int allowance = firstPageRows;

        while (start < arranged.Count)
        {
            int take = Math.Min(allowance, arranged.Count - start);
            pages.Add(arranged.Skip(start).Take(take).ToArray());
            start += take;
            allowance = rowsPerPage;
        }

        return pages.Count == 0 ? new[] { arranged } : pages;
    }
}
