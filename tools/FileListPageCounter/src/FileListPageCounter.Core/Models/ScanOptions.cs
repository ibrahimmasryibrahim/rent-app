namespace FileListPageCounter.Core.Models;

/// <summary>User-facing options that drive discovery and page counting.</summary>
public sealed class ScanOptions
{
    /// <summary>Include files inside sub-folders when a folder is selected.</summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>Drop unsupported file types from the result instead of listing them as "غير معروف".</summary>
    public bool IgnoreUnsupportedFiles { get; set; } = true;

    public SortMode SortMode { get; set; } = SortMode.ByFileName;

    /// <summary>
    /// A TIFF file may genuinely hold several pages. When true the frames are counted;
    /// when false every image — TIFF included — counts as exactly one page.
    /// </summary>
    public bool CountTiffFrames { get; set; } = true;

    /// <summary>Re-check name, size and last-write time of every source file after processing.</summary>
    public bool VerifyIntegrity { get; set; } = true;

    /// <summary>0 means "decide automatically from the processor count".</summary>
    public int MaxDegreeOfParallelism { get; set; }

    public int ResolveDegreeOfParallelism() =>
        MaxDegreeOfParallelism > 0
            ? MaxDegreeOfParallelism
            : Math.Clamp(Environment.ProcessorCount, 2, 8);
}
