using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;
using UglyToad.PdfPig;

namespace FileListPageCounter.Core.PageCounting;

/// <summary>
/// Counts PDF pages by reading the document's page tree only — the cross-reference table, the
/// catalog and the /Pages node. Page content streams, fonts and images are never decoded, so a
/// 500 MB scan bundle costs the same as a 50 KB one. The file is opened read-only and closed
/// unchanged; no save path exists in this code.
/// </summary>
public sealed class PdfPageCounter : IPageCounter
{
    private static readonly string[] SupportedExtensions = { ".pdf" };

    public IReadOnlyList<string> Extensions => SupportedExtensions;

    public PageCountResult Count(string path, ScanOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using FileStream stream = ReadOnlyFile.OpenRandomAccess(path);
            using PdfDocument document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = true });
            return PageCountResult.Counted(document.NumberOfPages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Damaged or unusually structured file: try the raw scan before giving up.
            int? fallback = RawPdfPageScanner.TryCount(path, cancellationToken);
            return fallback.HasValue
                ? PageCountResult.Counted(fallback.Value, $"تم الحساب بالطريقة الاحتياطية بعد خطأ: {ex.Message}")
                : PageCountResult.Failed(ex.Message);
        }
    }
}
