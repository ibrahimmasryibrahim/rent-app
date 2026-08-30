using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.PageCounting;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>Requirements 8 and 24: real page counts, and damaged files never stop the run.</summary>
public class PageCountingTests
{
    private readonly PageCounterRegistry _registry = PageCounterRegistry.CreateDefault();
    private readonly ScanOptions _options = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(64)]
    public void Pdf_page_count_is_exact(int pages)
    {
        using var temp = new TempFolder();
        string path = temp.File($"doc-{pages}.pdf");
        TestPdfFactory.Write(path, pages);

        PageCountResult result = _registry.Count(path, ".pdf", _options, CancellationToken.None);

        Assert.Equal(PageCountStatus.Counted, result.Status);
        Assert.Equal(pages, result.Pages);
    }

    [Fact]
    public void Images_count_as_one_page()
    {
        using var temp = new TempFolder();

        string jpg = temp.File("scan.jpg");
        string png = temp.File("scan.png");
        TestImageFactory.WriteJpeg(jpg);
        TestImageFactory.WritePng(png);

        Assert.Equal(1, _registry.Count(jpg, ".jpg", _options, CancellationToken.None).Pages);
        Assert.Equal(1, _registry.Count(png, ".png", _options, CancellationToken.None).Pages);
    }

    [Fact]
    public void Single_frame_tiff_counts_as_one_page()
    {
        using var temp = new TempFolder();
        string path = temp.File("scan.tif");
        TestImageFactory.WriteTiff(path, frames: 1);

        Assert.Equal(1, _registry.Count(path, ".tif", _options, CancellationToken.None).Pages);
    }

    [Fact]
    public void Multi_frame_tiff_counts_its_frames_when_the_option_is_on()
    {
        using var temp = new TempFolder();
        string path = temp.File("bundle.tiff");
        TestImageFactory.WriteTiff(path, frames: 5);

        var options = new ScanOptions { CountTiffFrames = true };
        Assert.Equal(5, _registry.Count(path, ".tiff", options, CancellationToken.None).Pages);
    }

    [Fact]
    public void Multi_frame_tiff_counts_as_one_page_when_the_option_is_off()
    {
        using var temp = new TempFolder();
        string path = temp.File("bundle.tiff");
        TestImageFactory.WriteTiff(path, frames: 5);

        var options = new ScanOptions { CountTiffFrames = false };
        Assert.Equal(1, _registry.Count(path, ".tiff", options, CancellationToken.None).Pages);
    }

    [Fact]
    public void A_corrupt_pdf_reports_unknown_instead_of_throwing()
    {
        using var temp = new TempFolder();
        string path = temp.File("broken.pdf");
        TestPdfFactory.WriteCorrupt(path);

        PageCountResult result = _registry.Count(path, ".pdf", _options, CancellationToken.None);

        Assert.Null(result.Pages);
        Assert.Equal(PageCountStatus.Error, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Note));
    }

    [Fact]
    public void An_unsupported_extension_is_reported_as_unsupported()
    {
        using var temp = new TempFolder();
        string path = temp.File("notes.txt");
        File.WriteAllText(path, "hello");

        PageCountResult result = _registry.Count(path, ".txt", _options, CancellationToken.None);

        Assert.Null(result.Pages);
        Assert.Equal(PageCountStatus.Unsupported, result.Status);
    }

    [Fact]
    public void An_empty_image_file_is_reported_as_an_error()
    {
        using var temp = new TempFolder();
        string path = temp.File("empty.png");
        File.WriteAllBytes(path, Array.Empty<byte>());

        PageCountResult result = _registry.Count(path, ".png", _options, CancellationToken.None);

        Assert.Null(result.Pages);
        Assert.Equal(PageCountStatus.Error, result.Status);
    }

    [Fact]
    public void The_registry_covers_every_format_promised_by_version_one()
    {
        foreach (string extension in new[] { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff" })
        {
            Assert.True(_registry.IsSupported(extension), extension + " must be supported");
        }
    }

    [Fact]
    public void A_new_format_can_be_added_without_touching_existing_code()
    {
        var registry = new PageCounterRegistry(new IPageCounter[] { new FakeCounter() });

        PageCountResult result = registry.Count("anything.xyz", ".xyz", _options, CancellationToken.None);

        Assert.Equal(42, result.Pages);
    }

    private sealed class FakeCounter : IPageCounter
    {
        public IReadOnlyList<string> Extensions => new[] { ".xyz" };

        public PageCountResult Count(string path, ScanOptions options, CancellationToken cancellationToken) =>
            PageCountResult.Counted(42);
    }
}
