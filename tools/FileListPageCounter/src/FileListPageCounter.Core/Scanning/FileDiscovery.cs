namespace FileListPageCounter.Core.Scanning;

/// <summary>
/// Finds the files to inspect. Enumeration is lazy and streaming, so a folder holding hundreds
/// of thousands of files never has to be materialised at once, and there is no artificial cap on
/// how many files may be processed.
/// </summary>
public static class FileDiscovery
{
    /// <summary>Lists the files of a folder, optionally including every sub-folder.</summary>
    public static IEnumerable<string> EnumerateFolder(string folder, bool includeSubdirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubdirectories,
            IgnoreInaccessible = true,                     // a locked sub-folder must not abort the scan
            AttributesToSkip = FileAttributes.ReparsePoint, // do not follow junctions/symlinks into loops
            MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false
        };

        return Directory.EnumerateFiles(folder, "*", options);
    }

    /// <summary>Normalises an explicit user selection of files, dropping anything that is not a real file.</summary>
    public static IEnumerable<string> FromSelection(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                continue;
            }

            if (seen.Add(full) && File.Exists(full))
            {
                yield return full;
            }
        }
    }
}
