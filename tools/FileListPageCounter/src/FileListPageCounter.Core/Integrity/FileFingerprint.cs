namespace FileListPageCounter.Core.Integrity;

/// <summary>The observable state of a source file that must not change: name, size, timestamps, attributes.</summary>
public readonly record struct FileFingerprint(
    string FullPath,
    string FileName,
    long Length,
    DateTime LastWriteTimeUtc,
    DateTime CreationTimeUtc,
    FileAttributes Attributes);
