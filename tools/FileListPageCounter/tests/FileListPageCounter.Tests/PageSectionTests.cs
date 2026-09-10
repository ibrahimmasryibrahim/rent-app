using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// Everything above and below the table can be switched off — the heading, the date, the band of
/// figures saying what was processed, the running header, the closing summary and the page
/// numbers — so a list can be printed with nothing on the page but the list itself.
/// </summary>
public class PageSectionTests
{
    private static List<ReportRow> Rows() => new()
    {
        new ReportRow(1, "ملف أول", 3),
        new ReportRow(2, "ملف ثانٍ", 5)
    };

    private static ReportOptions Everything() => new()
    {
        Title = "عنوان التقرير",
        UserName = string.Empty          // keep the name out of these assertions
    };

    // ------------------------------------------------------------------ Word

    private static (string Body, string Header, string Footer) WordParts(ReportOptions options)
    {
        using var output = new TempFolder();
        string path = output.File("report.docx");

        WordReportBuilder.Build(path, Rows(), options);

        using var document = WordprocessingDocument.Open(path, false);
        MainDocumentPart main = document.MainDocumentPart!;

        return (
            main.Document!.Body!.InnerText,
            string.Concat(main.HeaderParts.Select(h => h.Header!.InnerText)),
            string.Concat(main.FooterParts.Select(f => f.Footer!.InnerText)));
    }

    [Fact]
    public void Everything_is_on_the_page_unless_it_is_switched_off()
    {
        (string body, string header, string footer) = WordParts(Everything());

        Assert.Contains("عنوان التقرير", body, StringComparison.Ordinal);
        Assert.Contains("تاريخ الإنشاء", body, StringComparison.Ordinal);
        Assert.Contains("إجمالي عدد الملفات", body, StringComparison.Ordinal);
        Assert.Contains("الملخص", body, StringComparison.Ordinal);
        Assert.Contains("عنوان التقرير", header, StringComparison.Ordinal);
        Assert.Contains("صفحة", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_can_be_taken_off_the_first_page()
    {
        ReportOptions options = Everything();
        options.ShowTitle = false;

        (string body, string header, _) = WordParts(options);

        // The heading is gone from the page, but the running header still names the report.
        Assert.Contains("عنوان التقرير", header, StringComparison.Ordinal);
        Assert.DoesNotContain("عنوان التقرير", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_date_line_can_be_taken_off()
    {
        ReportOptions options = Everything();
        options.ShowDateLine = false;

        Assert.DoesNotContain("تاريخ الإنشاء", WordParts(options).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void What_was_processed_can_be_taken_off_the_top_of_the_page()
    {
        ReportOptions options = Everything();
        options.ShowTotalsBand = false;
        options.ShowSummary = false;

        string body = WordParts(options).Body;

        Assert.DoesNotContain("إجمالي عدد الملفات", body, StringComparison.Ordinal);
        Assert.DoesNotContain("الملخص", body, StringComparison.Ordinal);

        // The table itself is untouched.
        Assert.Contains("ملف أول", body, StringComparison.Ordinal);
        Assert.Contains("اسم الملف", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_running_header_can_be_taken_off_every_page()
    {
        ReportOptions options = Everything();
        options.ShowRunningHeader = false;

        Assert.Equal(string.Empty, WordParts(options).Header);
    }

    [Fact]
    public void Page_numbers_can_be_taken_off()
    {
        ReportOptions options = Everything();
        options.IncludePageNumbers = false;

        Assert.Equal(string.Empty, WordParts(options).Footer);
    }

    [Fact]
    public void A_bare_table_is_a_valid_document()
    {
        var options = new ReportOptions
        {
            ShowTitle = false,
            ShowDateLine = false,
            ShowTotalsBand = false,
            ShowRunningHeader = false,
            ShowSummary = false,
            IncludePageNumbers = false,
            ShowUserName = false
        };

        (string body, string header, string footer) = WordParts(options);

        Assert.Contains("ملف أول", body, StringComparison.Ordinal);
        Assert.Equal(string.Empty, header);
        Assert.Equal(string.Empty, footer);
    }

    // ----------------------------------------------------------------- Excel

    private static string SheetText(ReportOptions options)
    {
        using var output = new TempFolder();
        string path = output.File("report.xlsx");

        ExcelReportBuilder.Build(path, Rows(), options);

        using var document = SpreadsheetDocument.Open(path, false);
        return document.WorkbookPart!.WorksheetParts.Single().Worksheet.InnerText;
    }

    [Fact]
    public void The_same_switches_apply_to_the_workbook()
    {
        string full = SheetText(Everything());
        Assert.Contains("عنوان التقرير", full, StringComparison.Ordinal);
        Assert.Contains("تاريخ الإنشاء", full, StringComparison.Ordinal);
        Assert.Contains("إجمالي عدد الملفات", full, StringComparison.Ordinal);
        Assert.Contains("الإجمالي", full, StringComparison.Ordinal);

        var stripped = new ReportOptions
        {
            Title = "عنوان التقرير",
            UserName = string.Empty,
            ShowTitle = false,
            ShowDateLine = false,
            ShowTotalsBand = false,
            ShowSummary = false
        };

        string bare = SheetText(stripped);

        Assert.DoesNotContain("عنوان التقرير", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("تاريخ الإنشاء", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("إجمالي عدد الملفات", bare, StringComparison.Ordinal);

        // The table and its header survive.
        Assert.Contains("ملف أول", bare, StringComparison.Ordinal);
        Assert.Contains("اسم الملف", bare, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- copies

    [Fact]
    public void A_copy_carries_every_switch()
    {
        var original = new ReportOptions
        {
            Title = "س",
            FontSize = 24,
            ColumnBlocks = 3,
            RowsPerPage = 40,
            ShowTitle = false,
            ShowDateLine = false,
            ShowTotalsBand = false,
            ShowRunningHeader = false,
            ShowSummary = false,
            IncludePageNumbers = false,
            UserName = "اسم",
            ShowUserName = false
        };

        ReportOptions copy = original.Clone();

        Assert.Equal("س", copy.Title);
        Assert.Equal(24, copy.FontSize);
        Assert.Equal(3, copy.ColumnBlocks);
        Assert.Equal(40, copy.RowsPerPage);
        Assert.False(copy.ShowTitle);
        Assert.False(copy.ShowDateLine);
        Assert.False(copy.ShowTotalsBand);
        Assert.False(copy.ShowRunningHeader);
        Assert.False(copy.ShowSummary);
        Assert.False(copy.IncludePageNumbers);
        Assert.Equal("اسم", copy.UserName);
        Assert.False(copy.ShowUserName);

        // A copy is a separate object: changing it leaves the original alone.
        copy.ShowTitle = true;
        Assert.False(original.ShowTitle);
    }
}
