using FileListPageCounter.Core.Integrity;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Core.Scanning;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// Requirements 2, 3, 21, 22 and 23 — the non-negotiable part of the specification.
/// The source folder is READ ONLY: nothing is modified, renamed, moved, deleted, copied or
/// created there, and no timestamp or attribute changes.
/// </summary>
public class ReadOnlyGuaranteeTests
{
    private readonly ScanService _service = new();

    [Fact]
    public async Task Scanning_leaves_every_source_file_byte_for_byte_identical()
    {
        using var temp = new TempFolder();

        TestPdfFactory.Write(temp.File("a.pdf"), 3);
        TestPdfFactory.Write(temp.File("b.pdf"), 9);
        TestPdfFactory.WriteCorrupt(temp.File("c.pdf"));
        TestImageFactory.WriteTiff(temp.File("d.tiff"), frames: 4);
        TestImageFactory.WriteJpeg(temp.File("e.jpg"));
        File.WriteAllText(temp.File("f.txt"), "unsupported on purpose");

        string[] paths = Directory.GetFiles(temp.Path);
        var before = paths.ToDictionary(p => p, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, FileFingerprint> fingerprints = IntegrityVerifier.Capture(paths);

        await _service.ScanFolderAsync(temp.Path, new ScanOptions { IgnoreUnsupportedFiles = false });

        Assert.Empty(IntegrityVerifier.Verify(fingerprints));

        foreach ((string path, byte[] original) in before)
        {
            Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(path)), path + " changed on disk");
        }
    }

    [Fact]
    public async Task Last_write_time_and_creation_time_are_untouched()
    {
        using var temp = new TempFolder();
        string path = temp.File("archive.pdf");
        TestPdfFactory.Write(path, 12);

        // Push the timestamps into the past so any "touch" by the scanner would be obvious.
        var stamp = new DateTime(2019, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        File.SetCreationTimeUtc(path, stamp);
        File.SetLastAccessTimeUtc(path, stamp);

        FileAttributes attributesBefore = File.GetAttributes(path);

        await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(stamp, File.GetCreationTimeUtc(path));
        Assert.Equal(attributesBefore, File.GetAttributes(path));
    }

    [Fact]
    public async Task No_file_is_created_renamed_or_deleted_inside_the_source_folder()
    {
        using var temp = new TempFolder();
        TestPdfFactory.Write(temp.File("one.pdf"), 2);
        TestPdfFactory.Write(Path.Combine(temp.SubFolder("sub"), "two.pdf"), 3);
        TestImageFactory.WritePng(temp.File("three.png"));

        IReadOnlyCollection<string> before = IntegrityVerifier.SnapshotFolder(temp.Path, recurse: true);

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        // The report itself is written outside the source folder, as the application requires.
        using var output = new TempFolder();
        WordReportBuilder.Build(Path.Combine(output.Path, "report.docx"), result.Entries, new ReportOptions());

        IReadOnlyCollection<string> after = IntegrityVerifier.SnapshotFolder(temp.Path, recurse: true);

        Assert.Equal(before.OrderBy(x => x, StringComparer.Ordinal), after.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_read_only_source_folder_can_still_be_scanned()
    {
        using var temp = new TempFolder();
        string path = temp.File("locked.pdf");
        TestPdfFactory.Write(path, 5);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

            Assert.Equal(5, result.Entries.Single().PageCount);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal); // let the temp folder clean itself up
        }
    }

    [Fact]
    public async Task A_file_still_open_by_another_program_can_be_read()
    {
        using var temp = new TempFolder();
        string path = temp.File("in-use.pdf");
        TestPdfFactory.Write(path, 6);

        // Simulate another application holding the file open for writing.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());
            Assert.Equal(6, result.Entries.Single().PageCount);
        }
    }

    [Fact]
    public void The_integrity_verifier_actually_detects_a_change()
    {
        // Guards the guard: if this ever stops failing, the other tests here prove nothing.
        using var temp = new TempFolder();
        string path = temp.File("edited.pdf");
        TestPdfFactory.Write(path, 1);

        IReadOnlyDictionary<string, FileFingerprint> fingerprints = IntegrityVerifier.Capture(new[] { path });

        File.AppendAllText(path, "\n% modified by the test\n");

        IReadOnlyList<string> violations = IntegrityVerifier.Verify(fingerprints);

        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("حجم") || v.Contains("تاريخ التعديل"));
    }

    [Fact]
    public void Every_source_file_handle_the_core_opens_is_read_only()
    {
        // ReadOnlyFile is the only door to a source file; prove the door itself is locked open.
        using var temp = new TempFolder();
        string path = temp.File("probe.pdf");
        TestPdfFactory.Write(path, 1);

        using FileStream sequential = FileListPageCounter.Core.Common.ReadOnlyFile.OpenSequential(path);
        using FileStream random = FileListPageCounter.Core.Common.ReadOnlyFile.OpenRandomAccess(path);

        Assert.True(sequential.CanRead);
        Assert.False(sequential.CanWrite);
        Assert.True(random.CanRead);
        Assert.False(random.CanWrite);
    }

    [Fact]
    public async Task Files_shared_for_reading_only_are_still_counted()
    {
        // The test holds every file with FileShare.Read, which denies write access to everyone
        // else. Any counter that tried to open a source file for writing would hit a sharing
        // violation and fail to produce a count — so a clean result proves read-only access.
        using var temp = new TempFolder();

        string pdf = temp.File("guarded.pdf");
        string tiff = temp.File("guarded.tiff");
        string png = temp.File("guarded.png");

        TestPdfFactory.Write(pdf, 3);
        TestImageFactory.WriteTiff(tiff, frames: 2);
        TestImageFactory.WritePng(png);

        using var lock1 = new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lock2 = new FileStream(tiff, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lock3 = new FileStream(png, FileMode.Open, FileAccess.Read, FileShare.Read);

        ScanResult result = await _service.ScanFolderAsync(temp.Path, new ScanOptions());

        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(0, result.UnknownCount);
        Assert.Equal(6, result.TotalPages); // 3 + 2 + 1
    }
}
