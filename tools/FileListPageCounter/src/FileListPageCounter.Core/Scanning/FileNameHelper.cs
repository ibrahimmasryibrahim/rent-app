namespace FileListPageCounter.Core.Scanning;

public static class FileNameHelper
{
    /// <summary>
    /// The file name with the last extension removed, exactly as it must appear in the report:
    /// "123456.pdf" → "123456", "وثيقة 1001.pdf" → "وثيقة 1001", "Document-2026-001.pdf" →
    /// "Document-2026-001". Arabic letters, digits, spaces, dashes, underscores, brackets and
    /// every other character Windows permits are preserved untouched. Only the final extension
    /// is stripped, so "تقرير.نسخة.pdf" keeps "تقرير.نسخة". A name that is nothing but an
    /// extension (".gitignore") is returned unchanged rather than becoming empty.
    /// </summary>
    public static string GetDisplayName(string path)
    {
        string fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName)) return path;

        int lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0) return fileName; // no extension, or a leading-dot name like ".gitignore"

        return fileName[..lastDot];
    }

    /// <summary>Lower-cased extension including the dot, or an empty string when there is none.</summary>
    public static string GetExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) ? string.Empty : extension.ToLowerInvariant();
    }
}
