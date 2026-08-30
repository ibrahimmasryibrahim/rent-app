using System.Diagnostics;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Diagnostics;
using FileListPageCounter.Core.Integrity;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.PageCounting;

namespace FileListPageCounter.Core.Scanning;

/// <summary>
/// Drives one scan end to end: fingerprint the sources, count pages in parallel, sort, number the
/// rows and verify that nothing on disk changed. Everything here is read-only; no temporary,
/// cache or backup file is created anywhere, least of all in the source folder.
/// </summary>
public sealed class ScanService
{
    private readonly PageCounterRegistry _registry;
    private readonly IComparer<string> _nameComparer;

    public ScanService(PageCounterRegistry? registry = null, IComparer<string>? nameComparer = null)
    {
        _registry = registry ?? PageCounterRegistry.CreateDefault();
        _nameComparer = nameComparer ?? new NaturalComparer();
    }

    public PageCounterRegistry Registry => _registry;

    public Task<ScanResult> ScanFolderAsync(
        string folder,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ScanAsync(FileDiscovery.EnumerateFolder(folder, options.IncludeSubdirectories), options, progress, cancellationToken);

    public Task<ScanResult> ScanFilesAsync(
        IEnumerable<string> files,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ScanAsync(FileDiscovery.FromSelection(files), options, progress, cancellationToken);

    public async Task<ScanResult> ScanAsync(
        IEnumerable<string> paths,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var log = new ProcessingLog();

        // Materialise the paths once: the caller needs a total for the progress bar.
        List<string> allPaths = paths.ToList();

        IReadOnlyDictionary<string, FileFingerprint> before = options.VerifyIntegrity
            ? IntegrityVerifier.Capture(allPaths)
            : new Dictionary<string, FileFingerprint>();

        var entries = new FileEntry?[allPaths.Count];
        int processed = 0;
        int reportEvery = Math.Max(1, allPaths.Count / 200); // ~200 UI updates for any workload

        progress?.Report(new ScanProgress(0, allPaths.Count));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.ResolveDegreeOfParallelism(),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, allPaths.Count),
            parallelOptions,
            (i, token) =>
            {
                entries[i] = Inspect(allPaths[i], i, options, log, token);

                int done = Interlocked.Increment(ref processed);
                if (done == allPaths.Count || done % reportEvery == 0)
                {
                    progress?.Report(new ScanProgress(done, allPaths.Count));
                }

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        List<FileEntry> result = EntryOrganizer.Organize(
            entries.Where(static e => e is not null).Select(static e => e!),
            options,
            _nameComparer);

        IReadOnlyList<string> violations = options.VerifyIntegrity
            ? IntegrityVerifier.Verify(before)
            : Array.Empty<string>();

        foreach (string violation in violations)
        {
            log.Add(string.Empty, "تحذير سلامة الملفات: " + violation);
        }

        stopwatch.Stop();
        return new ScanResult(result, stopwatch.Elapsed, log, violations);
    }

    private FileEntry Inspect(string path, int discoveryOrder, ScanOptions options, ProcessingLog log, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(path);
        string extension = FileNameHelper.GetExtension(path);

        long size = 0;
        DateTime lastWriteUtc = default;

        try
        {
            var info = new FileInfo(path);
            size = info.Length;
            lastWriteUtc = info.LastWriteTimeUtc;
        }
        catch (Exception ex)
        {
            log.Add(path, "تعذر قراءة معلومات الملف: " + ex.Message);
        }

        var entry = new FileEntry
        {
            DiscoveryOrder = discoveryOrder,
            FullPath = path,
            FileName = fileName,
            DisplayName = FileNameHelper.GetDisplayName(path),
            Extension = extension,
            SizeBytes = size,
            LastWriteTimeUtc = lastWriteUtc
        };

        PageCountResult count = _registry.Count(path, extension, options, cancellationToken);

        entry.PageCount = count.Pages;
        entry.Status = count.Status;
        entry.Note = count.Note;

        if (count.Status == PageCountStatus.Error)
        {
            log.Add(path, "تعذر حساب عدد الصفحات: " + (count.Note ?? "سبب غير معروف"));
        }
        else if (count.Status == PageCountStatus.Unsupported)
        {
            log.Add(path, "نوع ملف غير مدعوم لحساب الصفحات: " + (string.IsNullOrEmpty(extension) ? "(بدون امتداد)" : extension));
        }
        else if (!string.IsNullOrEmpty(count.Note))
        {
            log.Add(path, count.Note!);
        }

        return entry;
    }
}
