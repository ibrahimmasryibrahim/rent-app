namespace FileListPageCounter.Core.Models;

public enum PageCountStatus
{
    /// <summary>The page count was determined successfully.</summary>
    Counted = 0,

    /// <summary>The extension has no registered counter (file type not supported).</summary>
    Unsupported = 1,

    /// <summary>The file is supported but unreadable or damaged.</summary>
    Error = 2
}
