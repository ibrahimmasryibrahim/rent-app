using DocumentFormat.OpenXml.Packaging;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Writes the Office document properties every report carries: the title the user chose and
/// the tool's author, so the credit survives even when the file is renamed or forwarded.
/// </summary>
internal static class DocumentProperties
{
    public static void Stamp(OpenXmlPackage package, ReportOptions options)
    {
        try
        {
            package.PackageProperties.Title = options.Title;
            package.PackageProperties.Subject = Common.Strings.ReportTitle;
            package.PackageProperties.Creator = options.DeveloperName;
            package.PackageProperties.LastModifiedBy = options.DeveloperName;
            package.PackageProperties.Created = DateTime.Now;
            package.PackageProperties.Modified = DateTime.Now;
        }
        catch (Exception)
        {
            // Metadata is a nicety; never fail a report over it.
        }
    }
}
