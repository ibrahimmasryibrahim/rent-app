using System.Globalization;

namespace FileListPageCounter.Core.Integrity;

/// <summary>
/// Proves the promise: source files are read-only. A fingerprint is taken before processing and
/// compared afterwards; any difference in name, size, timestamps or attributes is reported.
/// It also detects files that appeared in or disappeared from the source folder.
/// </summary>
public static class IntegrityVerifier
{
    public static IReadOnlyDictionary<string, FileFingerprint> Capture(IEnumerable<string> paths)
    {
        var map = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (TryFingerprint(path, out FileFingerprint fp))
            {
                map[path] = fp;
            }
        }

        return map;
    }

    public static bool TryFingerprint(string path, out FileFingerprint fingerprint)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                fingerprint = default;
                return false;
            }

            fingerprint = new FileFingerprint(
                info.FullName,
                info.Name,
                info.Length,
                info.LastWriteTimeUtc,
                info.CreationTimeUtc,
                info.Attributes);
            return true;
        }
        catch (Exception)
        {
            fingerprint = default;
            return false;
        }
    }

    /// <summary>Returns a human readable description of every difference. An empty list means nothing changed.</summary>
    public static IReadOnlyList<string> Verify(IReadOnlyDictionary<string, FileFingerprint> before)
    {
        var violations = new List<string>();

        foreach ((string path, FileFingerprint old) in before)
        {
            if (!TryFingerprint(path, out FileFingerprint now))
            {
                violations.Add($"الملف لم يعد موجودًا أو تعذر قراءته: {path}");
                continue;
            }

            if (!string.Equals(old.FileName, now.FileName, StringComparison.Ordinal))
            {
                violations.Add($"تغيّر اسم الملف: {path}");
            }

            if (old.Length != now.Length)
            {
                violations.Add($"تغيّر حجم الملف: {path} ({old.Length} → {now.Length})");
            }

            if (old.LastWriteTimeUtc != now.LastWriteTimeUtc)
            {
                violations.Add($"تغيّر تاريخ التعديل: {path} ({Fmt(old.LastWriteTimeUtc)} → {Fmt(now.LastWriteTimeUtc)})");
            }

            if (old.CreationTimeUtc != now.CreationTimeUtc)
            {
                violations.Add($"تغيّر تاريخ الإنشاء: {path} ({Fmt(old.CreationTimeUtc)} → {Fmt(now.CreationTimeUtc)})");
            }

            if (old.Attributes != now.Attributes)
            {
                violations.Add($"تغيّرت خصائص الملف: {path} ({old.Attributes} → {now.Attributes})");
            }
        }

        return violations;
    }

    /// <summary>Lists every file under <paramref name="folder"/> — used to prove no file was created there.</summary>
    public static IReadOnlyCollection<string> SnapshotFolder(string folder, bool recurse)
    {
        var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return new HashSet<string>(Directory.GetFiles(folder, "*", option), StringComparer.OrdinalIgnoreCase);
    }

    private static string Fmt(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
}
