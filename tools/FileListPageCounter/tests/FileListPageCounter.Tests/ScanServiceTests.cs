using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Scanning;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>Requirements 5, 6, 16, 17 and 24: selection, scale, ordering, filtering and resilience.</summary>
public class ScanServiceTests
{
    private readonly ScanService _service = new();

    [Fact]
    public async Task A_single_file_is_processed()
    {
        using var temp = new TempFolder();
        TestPdfFactory.Write(temp.File("only.pdf"), 4);

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        Assert.Single(result.Entries);
        Assert.Equal("only", result.Entries[0].DisplayName);
        Assert.Equal(4, result.Entries[0].PageCount);
        Assert.Equal(4, result.TotalPages);
    }

    [Fact]
    public async Task A_hundred_files_are_all_processed_with_the_right_totals()
    {
        using var temp = new TempFolder();

        for (int i = 1; i <= 100; i++)
        {
            TestPdfFactory.Write(temp.File($"{i:D4}.pdf"), pageCount: 3);
        }

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        Assert.Equal(100, result.TotalFiles);
        Assert.Equal(300, result.TotalPages);
        Assert.Equal(0, result.UnknownCount);
    }

    [Fact]
    public async Task A_large_batch_is_processed_and_progress_reaches_one_hundred_percent()
    {
        const int count = 1000;

        using var temp = new TempFolder();
        for (int i = 1; i <= count; i++)
        {
            TestImageFactory.WritePng(temp.File($"page-{i}.png"));
        }

        int lastProcessed = 0;
        var progress = new Progress<ScanProgress>(p => Volatile.Write(ref lastProcessed, p.Processed));

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions(), progress);

        Assert.Equal(count, result.TotalFiles);
        Assert.Equal(count, result.TotalPages);   // one page per image
        Assert.Equal(count, result.Entries.Select(e => e.Index).Max());
        Assert.Equal(Enumerable.Range(1, count), result.Entries.Select(e => e.Index));
    }

    [Fact]
    public async Task Multiple_selected_files_from_different_folders_are_processed()
    {
        using var temp = new TempFolder();
        string a = Path.Combine(temp.SubFolder("a"), "one.pdf");
        string b = Path.Combine(temp.SubFolder("b"), "two.pdf");
        TestPdfFactory.Write(a, 2);
        TestPdfFactory.Write(b, 5);

        ScanResult result = await _service.ScanFilesAsync(new[] { a, b }, new ScanOptions());

        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(7, result.TotalPages);
    }

    [Fact]
    public async Task Subfolders_are_included_only_when_the_option_is_on()
    {
        using var temp = new TempFolder();
        TestPdfFactory.Write(temp.File("root.pdf"), 1);
        TestPdfFactory.Write(Path.Combine(temp.SubFolder("nested"), "child.pdf"), 1);

        ScanResult withSub = await _service.ScanFolderAsync(temp.Path, new ScanOptions { IncludeSubdirectories = true });
        ScanResult withoutSub = await _service.ScanFolderAsync(temp.Path, new ScanOptions { IncludeSubdirectories = false });

        Assert.Equal(2, withSub.TotalFiles);
        Assert.Equal(1, withoutSub.TotalFiles);
        Assert.Equal("root", withoutSub.Entries[0].DisplayName);
    }

    [Fact]
    public async Task Unsupported_files_are_hidden_or_listed_as_unknown_depending_on_the_option()
    {
        using var temp = new TempFolder();
        TestPdfFactory.Write(temp.File("doc.pdf"), 2);
        File.WriteAllText(temp.File("readme.txt"), "not a document");

        ScanResult hidden = await _service.ScanFolderAsync(temp.Path, new ScanOptions { IgnoreUnsupportedFiles = true });
        ScanResult listed = await _service.ScanFolderAsync(temp.Path, new ScanOptions { IgnoreUnsupportedFiles = false });

        Assert.Single(hidden.Entries);

        Assert.Equal(2, listed.TotalFiles);
        FileEntry unsupported = listed.Entries.Single(e => e.DisplayName == "readme");
        Assert.Null(unsupported.PageCount);
        Assert.Equal("غير معروف", unsupported.PageCountText);
    }

    [Fact]
    public async Task A_damaged_file_does_not_stop_the_others()
    {
        using var temp = new TempFolder();
        TestPdfFactory.Write(temp.File("1001.pdf"), 4);
        TestPdfFactory.Write(temp.File("1002.pdf"), 6);
        TestPdfFactory.WriteCorrupt(temp.File("1003.pdf"));
        TestPdfFactory.Write(temp.File("1004.pdf"), 8);

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        Assert.Equal(4, result.TotalFiles);
        Assert.Equal(18, result.TotalPages);
        Assert.Equal(1, result.UnknownCount);
        Assert.Equal("غير معروف", result.Entries.Single(e => e.DisplayName == "1003").PageCountText);
        Assert.True(result.Log.Count > 0, "the failure should be recorded in the processing log");
    }

    [Fact]
    public async Task Rows_are_numbered_naturally_by_file_name_by_default()
    {
        using var temp = new TempFolder();
        foreach (int n in new[] { 1, 2, 3, 10, 11, 20 })
        {
            TestPdfFactory.Write(temp.File($"{n}.pdf"), 1);
        }

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        Assert.Equal(
            new[] { "1", "2", "3", "10", "11", "20" },
            result.Entries.Select(e => e.DisplayName).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, result.Entries.Select(e => e.Index).ToArray());
    }

    [Fact]
    public async Task Folder_order_keeps_the_discovery_sequence()
    {
        using var temp = new TempFolder();
        string[] paths =
        {
            temp.File("zebra.pdf"),
            temp.File("alpha.pdf"),
            temp.File("mike.pdf")
        };

        foreach (string p in paths) TestPdfFactory.Write(p, 1);

        ScanResult result = await _service.ScanFilesAsync(paths, new ScanOptions { SortMode = SortMode.FolderOrder });

        Assert.Equal(new[] { "zebra", "alpha", "mike" }, result.Entries.Select(e => e.DisplayName).ToArray());
    }

    [Fact]
    public async Task A_scan_can_be_cancelled()
    {
        using var temp = new TempFolder();
        for (int i = 0; i < 200; i++) TestImageFactory.WritePng(temp.File($"img-{i}.png"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ScanFolderAsync(temp.Path, new ScanOptions(), progress: null, cancellationToken: cts.Token));
    }

    [Fact]
    public void The_view_can_be_re_filtered_and_re_sorted_without_reading_the_disk_again()
    {
        var entries = new List<FileEntry>
        {
            Entry("10", ".pdf", 1, PageCountStatus.Counted, order: 0),
            Entry("2", ".pdf", 1, PageCountStatus.Counted, order: 1),
            Entry("notes", ".txt", null, PageCountStatus.Unsupported, order: 2)
        };

        List<FileEntry> hidden = EntryOrganizer.Organize(entries, new ScanOptions { IgnoreUnsupportedFiles = true });
        Assert.Equal(new[] { "2", "10" }, hidden.Select(e => e.DisplayName).ToArray());

        List<FileEntry> listed = EntryOrganizer.Organize(entries, new ScanOptions { IgnoreUnsupportedFiles = false, SortMode = SortMode.FolderOrder });
        Assert.Equal(new[] { "10", "2", "notes" }, listed.Select(e => e.DisplayName).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, listed.Select(e => e.Index).ToArray());
    }

    private static FileEntry Entry(string name, string extension, int? pages, PageCountStatus status, int order)
    {
        var entry = new FileEntry
        {
            DiscoveryOrder = order,
            FullPath = Path.Combine("C:", "archive", name + extension),
            FileName = name + extension,
            DisplayName = name,
            Extension = extension
        };

        // The setters are internal; the test assembly is a friend (see InternalsVisibleTo).
        entry.PageCount = pages;
        entry.Status = status;
        return entry;
    }
}
