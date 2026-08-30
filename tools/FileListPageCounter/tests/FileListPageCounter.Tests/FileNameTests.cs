using FileListPageCounter.Core.Scanning;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>Requirement 7: the report shows the file name with the last extension removed.</summary>
public class FileNameTests
{
    [Theory]
    [InlineData("123456.pdf", "123456")]
    [InlineData("123456.jpg", "123456")]
    [InlineData("وثيقة 1001.pdf", "وثيقة 1001")]
    [InlineData("Document-2026-001.pdf", "Document-2026-001")]
    [InlineData("عقد_إيجار (نسخة 2).PDF", "عقد_إيجار (نسخة 2)")]
    [InlineData("تقرير.نسخة.pdf", "تقرير.نسخة")]      // only the LAST extension is removed
    [InlineData("no-extension", "no-extension")]
    [InlineData(".gitignore", ".gitignore")]           // never collapses to an empty name
    public void GetDisplayName_removes_only_the_last_extension(string fileName, string expected)
    {
        string path = Path.Combine("C:", "archive", fileName);
        Assert.Equal(expected, FileNameHelper.GetDisplayName(path));
    }

    [Theory]
    [InlineData("a.PDF", ".pdf")]
    [InlineData("a.TiFf", ".tiff")]
    [InlineData("a", "")]
    public void GetExtension_is_lower_cased(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameHelper.GetExtension(fileName));
    }
}
