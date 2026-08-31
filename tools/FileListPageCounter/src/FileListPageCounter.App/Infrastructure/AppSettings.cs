using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.App.Infrastructure;

/// <summary>
/// User preferences. Stored next to the executable when a file named <c>portable.txt</c> sits
/// beside it (portable mode), otherwise under %APPDATA%. Never inside a scanned source folder.
/// </summary>
public sealed class AppSettings
{
    public bool IncludeSubdirectories { get; set; } = true;

    public bool IgnoreUnsupportedFiles { get; set; } = true;

    public bool CountTiffFrames { get; set; } = true;

    public bool VerifyIntegrity { get; set; } = true;

    public int FontSize { get; set; } = ReportOptions.DefaultFontSize;

    public SortMode SortMode { get; set; } = SortMode.ByFileName;

    public bool OpenAfterCreate { get; set; } = true;

    /// <summary>Use the selected folder's own name as the report heading.</summary>
    public bool UseFolderNameAsTitle { get; set; } = true;

    [JsonIgnore]
    public static string SettingsPath
    {
        get
        {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);

            if (!string.IsNullOrEmpty(exeDir) && File.Exists(Path.Combine(exeDir, "portable.txt")))
            {
                return Path.Combine(exeDir, "settings.json");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileListPageCounter",
                "settings.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path)) return new AppSettings();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A damaged or unreadable settings file must never stop the application.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            string path = SettingsPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(true));
        }
        catch (Exception)
        {
            // Preferences are a convenience; failing to persist them is not worth an error dialog.
        }
    }
}
