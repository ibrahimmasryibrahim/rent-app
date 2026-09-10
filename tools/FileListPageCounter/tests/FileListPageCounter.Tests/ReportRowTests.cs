using System.ComponentModel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Integrity;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Core.Scanning;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// The editable preview rows: everything the user can change before saving or printing, and the
/// guarantee that changing any of it leaves the files on disk exactly as they were.
/// </summary>
public class ReportRowTests
{
    [Fact]
    public void A_row_starts_as_a_copy_of_what_was_read_from_the_file()
    {
        using var temp = new TempFolder();
        string path = temp.File("وثيقة 1001.pdf");
        TestPdfFactory.Write(path, 7);

        var entry = new FileEntry
        {
            DiscoveryOrder = 0,
            FullPath = path,
            FileName = "وثيقة 1001.pdf",
            DisplayName = "وثيقة 1001",
            Extension = ".pdf"
        };

        var row = ReportRow.From(entry);

        Assert.Equal("وثيقة 1001", row.Name);
        Assert.Equal(path, row.SourcePath);
        Assert.True(row.HasSource);
    }

    [Fact]
    public void Editing_a_row_cannot_reach_the_file_it_came_from()
    {
        using var temp = new TempFolder();
        string path = temp.File("12345.pdf");
        TestPdfFactory.Write(path, 4);

        var stamp = new DateTime(2019, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        byte[] before = File.ReadAllBytes(path);
        IReadOnlyDictionary<string, FileFingerprint> fingerprints = IntegrityVerifier.Capture(new[] { path });

        var row = new ReportRow(1, "12345", 4, path);

        // The user renames the line and corrects its count.
        row.Name = "12345 - مراجعة";
        row.PageCount = 9;
        row.Index = 42;

        Assert.Equal("12345 - مراجعة", row.Name);
        Assert.Equal(9, row.PageCount);

        // The file on disk has not moved a byte.
        Assert.Equal("12345.pdf", Path.GetFileName(path));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Empty(IntegrityVerifier.Verify(fingerprints));
    }

    [Fact]
    public void The_page_count_cell_accepts_a_number_and_takes_a_blank_as_undetermined()
    {
        var row = new ReportRow(1, "doc", 5);

        Assert.Equal("5", row.PageCountText);

        row.PageCountText = "12";
        Assert.Equal(12, row.PageCount);

        row.PageCountText = "   ";
        Assert.Null(row.PageCount);
        Assert.Equal(Strings.Unknown, row.PageCountText);

        row.PageCountText = "8";
        Assert.Equal(8, row.PageCount);

        row.PageCountText = Strings.Unknown;
        Assert.Null(row.PageCount);
    }

    [Fact]
    public void Arabic_digits_are_understood_in_the_page_count_cell()
    {
        var row = new ReportRow(1, "doc", null);

        row.PageCountText = "٢٥";
        Assert.Equal(25, row.PageCount);

        row.PageCountText = "۳۰";
        Assert.Equal(30, row.PageCount);
    }

    [Fact]
    public void Nonsense_in_the_page_count_cell_becomes_undetermined_rather_than_an_error()
    {
        var row = new ReportRow(1, "doc", 3);

        row.PageCountText = "ثلاثة";
        Assert.Null(row.PageCount);
        Assert.Equal(Strings.Unknown, row.PageCountText);
    }

    [Fact]
    public void A_row_added_by_hand_has_no_file_behind_it()
    {
        var row = new ReportRow(1, string.Empty, null);

        Assert.False(row.HasSource);
        Assert.Null(row.SourcePath);
        Assert.Equal(Strings.Unknown, row.PageCountText);
    }

    [Fact]
    public void Every_edit_announces_itself_so_the_preview_can_follow()
    {
        var row = new ReportRow(1, "doc", 1);
        var seen = new List<string>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => seen.Add(e.PropertyName ?? string.Empty);

        row.Name = "renamed";
        row.Index = 5;
        row.PageCount = 9;

        Assert.Contains(nameof(ReportRow.Name), seen);
        Assert.Contains(nameof(ReportRow.Index), seen);
        Assert.Contains(nameof(ReportRow.PageCount), seen);
        Assert.Contains(nameof(ReportRow.PageCountText), seen);
    }

    [Fact]
    public void The_totals_follow_the_edited_rows_not_the_scanned_files()
    {
        var rows = new List<ReportRow>
        {
            new(1, "a", 4),
            new(2, "b", 7),
            new(3, "c", null)
        };

        ReportTotals before = ReportTotals.From(rows);
        Assert.Equal(3, before.Files);
        Assert.Equal(11, before.Pages);
        Assert.Equal(1, before.Unknown);

        // The user fills in the missing count and corrects another.
        rows[2].PageCount = 5;
        rows[0].PageCount = 6;

        ReportTotals after = ReportTotals.From(rows);
        Assert.Equal(18, after.Pages);
        Assert.Equal(0, after.Unknown);
    }

    [Fact]
    public async Task An_edited_table_is_what_reaches_the_report()
    {
        using var source = new TempFolder();
        using var output = new TempFolder();

        TestPdfFactory.Write(source.File("10001.pdf"), 4);
        TestPdfFactory.Write(source.File("10002.pdf"), 7);

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());
        List<ReportRow> rows = ReportRow.From(result.Entries);

        rows[0].Name = "عقد مراجَع";
        rows[0].PageCount = 99;
        rows.Add(new ReportRow(3, "سطر أضافه المستخدم", 2));

        string path = output.File("edited.docx");
        WordReportBuilder.Build(path, rows, new ReportOptions());

        using var document = WordprocessingDocument.Open(path, false);
        string text = document.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("عقد مراجَع", text, StringComparison.Ordinal);
        Assert.Contains("سطر أضافه المستخدم", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10001", text, StringComparison.Ordinal);   // the old name is gone

        // 99 + 7 + 2, straight from the edited rows.
        Assert.Contains("إجمالي عدد الصفحات: 108", text, StringComparison.Ordinal);

        // And the sources are still exactly as they were found.
        Assert.Equal(
            new[] { "10001.pdf", "10002.pdf" },
            Directory.GetFiles(source.Path).Select(Path.GetFileName).OrderBy(n => n).ToArray());
    }
}
