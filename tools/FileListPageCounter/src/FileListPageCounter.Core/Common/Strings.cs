namespace FileListPageCounter.Core.Common;

/// <summary>
/// Arabic strings shared between the report writer and the UI.
/// Kept in one place so wording stays consistent everywhere.
/// </summary>
public static class Strings
{
    public const string Unknown = "غير معروف";
    public const string ReportTitle = "قائمة الملفات وعدد الصفحات";

    /// <summary>Author of the tool. Shown in the application window only — never inside a report.</summary>
    public const string Developer = "Ibrahim Masry Ibrahim";

    public const string ColumnIndex = "م";
    public const string ColumnFileName = "اسم الملف";
    public const string ColumnPages = "عدد الصفحات";

    public const string TotalFiles = "إجمالي عدد الملفات";
    public const string TotalPages = "إجمالي عدد الصفحات";
    public const string UnknownFiles = "عدد الملفات التي تعذر تحديد صفحاتها";
    public const string Summary = "الملخص";
    public const string GrandTotal = "الإجمالي";
    public const string PageOf = "صفحة";
    public const string WorksheetName = "الملفات";

    /// <summary>Builds a save-dialog file name from the report title, dropping characters Windows rejects.</summary>
    public static string SuggestFileName(string title, string extension)
    {
        string name = string.IsNullOrWhiteSpace(title) ? ReportTitle : title.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, ' ');
        }

        name = name.Trim();
        if (name.Length == 0) name = ReportTitle;
        if (name.Length > 120) name = name[..120].Trim();

        return name + extension;
    }
}
