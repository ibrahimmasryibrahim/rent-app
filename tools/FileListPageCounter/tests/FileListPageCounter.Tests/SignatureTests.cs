using DocumentFormat.OpenXml.Packaging;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// The name at the foot of the page: printed on its own, with no word in front of it, and never
/// anything about who wrote the program.
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
    public void The_name_is_on_the_page_by_default()
    {
        var options = new ReportOptions();

        Assert.Equal("IBRAHIM MASRY IBRAHIM", options.UserName);
        Assert.True(options.ShowUserName);
        Assert.True(options.HasUserSignature);
    }

    [Fact]
    public void Clearing_the_name_leaves_nothing_to_print()
    {
        Assert.False(new ReportOptions { UserName = string.Empty }.HasUserSignature);
        Assert.False(new ReportOptions { UserName = "   " }.HasUserSignature);
        Assert.False(new ReportOptions { ShowUserName = false }.HasUserSignature);
        Assert.True(new ReportOptions { UserName = "Ibrahim" }.HasUserSignature);
    }

    [Fact]
    public void The_name_is_printed_on_its_own_with_no_word_before_it()
    {
        using var output = new TempFolder();
        string path = output.File("signed.docx");

        WordReportBuilder.Build(path, Rows(), new ReportOptions { UserName = "IBRAHIM MASRY IBRAHIM" });

        string footer = FooterText(path);

        Assert.Contains("IBRAHIM MASRY IBRAHIM", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("إعداد", footer, StringComparison.Ordinal);

        // Nothing at all sits between the page number and the name.
        int nameStart = footer.IndexOf("IBRAHIM MASRY IBRAHIM", StringComparison.Ordinal);
        string beforeName = footer[..nameStart];
        Assert.DoesNotContain(":", beforeName, StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_the_name_off_removes_it()
    {
        using var output = new TempFolder();
        string path = output.File("plain.docx");

        WordReportBuilder.Build(path, Rows(), new ReportOptions { ShowUserName = false });

        Assert.DoesNotContain("IBRAHIM", FooterText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_name_still_prints_when_page_numbers_are_switched_off()
    {
        using var output = new TempFolder();
        string path = output.File("no-numbers.docx");

        WordReportBuilder.Build(path, Rows(), new ReportOptions
        {
            IncludePageNumbers = false,
            UserName = "IBRAHIM MASRY IBRAHIM"
        });

        string footer = FooterText(path);

        Assert.Contains("IBRAHIM MASRY IBRAHIM", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("صفحة", footer, StringComparison.Ordinal);
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

        WordReportBuilder.Build(path, Rows(), new ReportOptions { UserName = "سامي" });

        using var document = WordprocessingDocument.Open(path, false);
        string everything = document.MainDocumentPart!.Document!.Body!.InnerText + FooterText(path);

        foreach (string forbidden in new[] { "Developer", "Developed by", "Created by", "Programmer", "مطور", "المبرمج", "إعداد" })
        {
            Assert.DoesNotContain(forbidden, everything, StringComparison.OrdinalIgnoreCase);
        }

        // The document author is the tool, not a person.
        Assert.Equal("FILE LIST & PAGE COUNTER", document.PackageProperties.Creator);
    }
}
