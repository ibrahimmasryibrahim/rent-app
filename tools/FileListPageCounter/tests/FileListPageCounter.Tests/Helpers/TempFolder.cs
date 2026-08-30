namespace FileListPageCounter.Tests.Helpers;

/// <summary>
/// A scratch folder inside the system temp directory. Nothing in the production code is ever
/// allowed to write here either — the tests create the fixtures, the code only reads them.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder(string? name = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "flpc-tests",
            name ?? Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relativeName) => System.IO.Path.Combine(Path, relativeName);

    public string SubFolder(string name)
    {
        string dir = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A locked file in a scratch folder must not fail a test run.
        }
    }
}
