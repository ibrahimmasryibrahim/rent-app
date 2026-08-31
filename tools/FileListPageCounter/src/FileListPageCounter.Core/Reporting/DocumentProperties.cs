using DocumentFormat.OpenXml.Packaging;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.Core.Reporting;

/// <summary>
/// Writes the Office document properties every report carries. The author field names the tool
/// that generated the file, never a person: nobody signed this report, it was produced from a
/// folder listing, and claiming a preparer in the metadata would be a claim the file cannot make.
/// </summary>
internal static class DocumentProperties
{
    private const string ProducedBy = "FILE LIST & PAGE COUNTER";


    public static void Stamp(OpenXmlPackage package, ReportOptions options)
    {
        try
        {
            package.PackageProperties.Title = options.Title;
            package.PackageProperties.Subject = Common.Strings.ReportTitle;
            package.PackageProperties.Creator = ProducedBy;
            package.PackageProperties.LastModifiedBy = ProducedBy;
            package.PackageProperties.Created = DateTime.Now;
            package.PackageProperties.Modified = DateTime.Now;
        }
        catch (Exception)
        {
            // Metadata is a nicety; never fail a report over it.
        }
    }
}
