using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.PageCounting;

/// <summary>
/// Every image file counts as one page — the archive convention for scanned documents.
/// The one exception is TIFF, the only listed image format that can genuinely hold several
/// pages in a single file: when <see cref="ScanOptions.CountTiffFrames"/> is on, its frames are
/// counted; when it is off, a TIFF counts as one page like any other image.
/// No image is ever decoded; the file is only opened to confirm it is readable and non-empty.
/// </summary>
public sealed class ImagePageCounter : IPageCounter
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png",
        ".tif", ".tiff",
        ".bmp", ".gif", ".webp"
    };

    private static readonly HashSet<string> TiffExtensions =
        new(new[] { ".tif", ".tiff" }, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Extensions => SupportedExtensions;

    public PageCountResult Count(string path, ScanOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string extension = Path.GetExtension(path);

        if (options.CountTiffFrames && TiffExtensions.Contains(extension))
        {
            int? frames = TiffFrameCounter.TryCount(path, cancellationToken);
            if (frames.HasValue)
            {
                return PageCountResult.Counted(frames.Value);
            }
            // Not a readable TIFF structure — fall through to the "one image, one page" rule.
        }

        try
        {
            using FileStream stream = ReadOnlyFile.OpenSequential(path);
            if (stream.Length == 0)
            {
                return PageCountResult.Failed("الملف فارغ (0 بايت).");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PageCountResult.Failed(ex.Message);
        }

        return PageCountResult.Counted(1);
    }
}
