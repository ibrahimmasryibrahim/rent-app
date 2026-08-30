using System.IO;

namespace FileListPageCounter.Core.Common;

/// <summary>
/// The single door through which this application is allowed to touch a source file.
/// Every stream handed out here is opened with <see cref="FileAccess.Read"/> only, so the
/// operating system itself rejects any accidental write, truncate or attribute change.
/// Nothing in the code base may open a source file by any other means.
/// </summary>
public static class ReadOnlyFile
{
    private const int BufferSize = 64 * 1024;

    /// <summary>Opens a source file strictly for reading, with sequential access hints.</summary>
    public static FileStream OpenSequential(string path) => Open(path, FileOptions.SequentialScan);

    /// <summary>Opens a source file strictly for reading, with random access hints (PDF structure reads).</summary>
    public static FileStream OpenRandomAccess(string path) => Open(path, FileOptions.RandomAccess);

    private static FileStream Open(string path, FileOptions options) =>
        new(
            path,
            FileMode.Open,                                   // never Create / CreateNew / Truncate / Append
            FileAccess.Read,                                 // never Write / ReadWrite
            FileShare.ReadWrite | FileShare.Delete,          // do not lock the file for other applications
            BufferSize,
            options);
}
