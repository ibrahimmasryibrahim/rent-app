using DocumentFormat.OpenXml.Packaging;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// The optional name at the foot of the page: the user's own, only if they ask for it, and
/// never anything about who wrote the program.
/// </summary>
public class SignatureTests
{
    private static List<ReportRow> Rows() => new()
    {
        new ReportRow(1, "أ", 3),
        new ReportRow(2, "ب", 5)
    };

    private static string FooterText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return string.Concat(document.MainDocumentPart!.FooterParts.Select(f => f.Footer!.InnerText));
    }

    [Fact]
    public void A_report_is_unsigned_by_default()
    {
        Assert.False(new ReportOptions().ShowUserName);
        Assert.Equal(string.Empty, new ReportOptions().UserName);
        Assert.False(new ReportOptions().HasUserSignature);
    }

    [Fact]
    public void Asking_for_a_signature_without_typing_a_name_signs_nothing()
    {
        Assert.False(new ReportOptions { ShowUserName = true }.HasUserSignature);
        Assert.False(new ReportOptions { ShowUserName = true, UserName = "   " }.HasUserSignature);
        Assert.True(new ReportOptions { ShowUserName = true, UserName = "Ibrahim" }.HasUserSignature);
    }

    [Fact]
    public void The_name_appears_at_the_foot_of_the_page_only_when_it_was_asked_for()
    {
        using var output = new TempFolder();

        string without = output.File("plain.docx");
        WordReportBuilder.Build(without, Rows(), new ReportOptions { UserName = "Ibrahim" });
        Assert.DoesNotContain("Ibrahim", FooterText(without), StringComparison.Ordinal);

        string with = output.File("signed.docx");
        WordReportBuilder.Build(with, Rows(), new ReportOptions { ShowUserName = true, UserName = "Ibrahim" });

        string footer = FooterText(with);
        Assert.Contains("إعداد: Ibrahim", footer, StringComparison.Ordinal);
        Assert.Contains("صفحة", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_signature_is_set_small_enough_to_stay_out_of_the_way()
    {
        Assert.InRange(ReportOptions.SignatureFontSize, 8, 9);
    }

    [Fact]
    public void No_report_ever_mentions_the_programmer()
    {
        using var output = new TempFolder();
        string path = output.File("report.docx");

        WordReportBuilder.Build(path, Rows(), new ReportOptions { ShowUserName = true, UserName = "سامي" });

        using var document = WordprocessingDocument.Open(path, false);
        string everything = document.MainDocumentPart!.Document!.Body!.InnerText + FooterText(path);

        foreach (string forbidden in new[] { "Developer", "Developed by", "Created by", "Programmer", "مطور", "المبرمج" })
        {
            Assert.DoesNotContain(forbidden, everything, StringComparison.OrdinalIgnoreCase);
        }

        // The document author is the tool, not a person.
        Assert.Equal("FILE LIST & PAGE COUNTER", document.PackageProperties.Creator);
    }
}
