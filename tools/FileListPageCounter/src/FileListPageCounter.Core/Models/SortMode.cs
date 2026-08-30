namespace FileListPageCounter.Core.Models;

public enum SortMode
{
    /// <summary>Natural sort on the file name without extension (default).</summary>
    ByFileName = 0,

    /// <summary>Keep the order the files were discovered in / selected in.</summary>
    FolderOrder = 1
}
