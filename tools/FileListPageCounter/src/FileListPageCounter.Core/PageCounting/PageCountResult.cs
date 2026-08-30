using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.PageCounting;

public readonly record struct PageCountResult(int? Pages, PageCountStatus Status, string? Note)
{
    public static PageCountResult Counted(int pages, string? note = null) =>
        new(pages, PageCountStatus.Counted, note);

    public static PageCountResult Failed(string note) =>
        new(null, PageCountStatus.Error, note);

    public static PageCountResult Unsupported() =>
        new(null, PageCountStatus.Unsupported, null);
}
