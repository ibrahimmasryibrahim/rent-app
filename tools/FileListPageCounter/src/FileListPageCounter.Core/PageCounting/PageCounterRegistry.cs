using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.PageCounting;

/// <summary>
/// Maps an extension to the counter that handles it. New formats are added by registering
/// another <see cref="IPageCounter"/> — no other part of the application changes.
/// </summary>
public sealed class PageCounterRegistry
{
    private readonly Dictionary<string, IPageCounter> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public PageCounterRegistry(IEnumerable<IPageCounter> counters)
    {
        foreach (IPageCounter counter in counters)
        {
            foreach (string ext in counter.Extensions)
            {
                _byExtension[ext] = counter;
            }
        }
    }

    /// <summary>The formats shipped with version 1: PDF plus the common archive image formats.</summary>
    public static PageCounterRegistry CreateDefault() =>
        new(new IPageCounter[] { new PdfPageCounter(), new ImagePageCounter() });

    public IReadOnlyCollection<string> SupportedExtensions => _byExtension.Keys;

    public bool IsSupported(string extension) => _byExtension.ContainsKey(extension);

    public PageCountResult Count(string path, string extension, ScanOptions options, CancellationToken cancellationToken)
    {
        if (!_byExtension.TryGetValue(extension, out IPageCounter? counter))
        {
            return PageCountResult.Unsupported();
        }

        try
        {
            return counter.Count(path, options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Belt and braces: a counter must not be able to abort the whole scan.
            return PageCountResult.Failed(ex.Message);
        }
    }
}
