using System.Windows;
using System.Windows.Documents;

namespace FileListPageCounter.App.Printing;

/// <summary>
/// Prints a slice of a document. WPF's print dialog carries a page range but
/// <see cref="System.Windows.Controls.PrintDialog.PrintDocument"/> ignores it, so the range has
/// to be applied by wrapping the paginator itself.
/// </summary>
public sealed class PageRangePaginator : DocumentPaginator
{
    private readonly DocumentPaginator _inner;
    private readonly int _first;   // zero-based
    private readonly int _count;

    public PageRangePaginator(DocumentPaginator inner, int firstPage, int lastPage)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;

        int total = inner.IsPageCountValid ? inner.PageCount : int.MaxValue;

        int from = Math.Max(1, Math.Min(firstPage, lastPage));
        int to = Math.Min(total, Math.Max(firstPage, lastPage));

        _first = from - 1;
        _count = Math.Max(0, to - from + 1);
    }

    public override DocumentPage GetPage(int pageNumber) => _inner.GetPage(_first + pageNumber);

    public override bool IsPageCountValid => true;

    public override int PageCount => _count;

    public override Size PageSize
    {
        get => _inner.PageSize;
        set => _inner.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => _inner.Source;
}
