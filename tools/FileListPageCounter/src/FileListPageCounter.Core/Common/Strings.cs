namespace FileListPageCounter.Core.Common;

/// <summary>
/// Arabic strings shared between the report writer and the UI.
/// Kept in one place so wording stays consistent everywhere.
/// </summary>
public static class Strings
{
    public const string Unknown = "غير معروف";
    public const string ReportTitle = "قائمة الملفات وعدد الصفحات";
    public const string DefaultFileName = "قائمة الملفات وعدد الصفحات.docx";

    public const string ColumnIndex = "م";
    public const string ColumnFileName = "اسم الملف";
    public const string ColumnPages = "عدد الصفحات";

    public const string TotalFiles = "إجمالي عدد الملفات";
    public const string TotalPages = "إجمالي عدد الصفحات";
    public const string UnknownFiles = "عدد الملفات التي تعذر تحديد صفحاتها";
    public const string Summary = "الملخص";
    public const string PageOf = "صفحة";
}
