using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>Report title handling and the file name suggested in the save dialog.</summary>
public class ReportNamingTests
{
    [Fact]
    public void The_default_title_is_the_standard_heading()
    {
        Assert.Equal("قائمة الملفات وعدد الصفحات", new ReportOptions().Title);
    }

    [Theory]
    [InlineData("أرشيف 2026", "أرشيف 2026")]
    [InlineData("  عقود الإيجار  ", "عقود الإيجار")]   // trimmed
    [InlineData("", "قائمة الملفات وعدد الصفحات")]     // blank falls back
    [InlineData("   ", "قائمة الملفات وعدد الصفحات")]
    public void The_title_is_trimmed_and_never_left_empty(string given, string expected)
    {
        Assert.Equal(expected, new ReportOptions { Title = given }.Title);
    }

    [Fact]
    public void The_developer_name_is_carried_by_default()
    {
        Assert.Equal("Ibrahim Masry Ibrahim", new ReportOptions().DeveloperName);
        Assert.Equal("Ibrahim Masry Ibrahim", Strings.Developer);
    }

    [Theory]
    [InlineData("أرشيف 2026", ".docx", "أرشيف 2026.docx")]
    [InlineData("عقود الإيجار", ".xlsx", "عقود الإيجار.xlsx")]
    [InlineData("Reports 2026", ".xlsx", "Reports 2026.xlsx")]
    public void The_save_dialog_suggests_the_title_as_the_file_name(string title, string extension, string expected)
    {
        Assert.Equal(expected, Strings.SuggestFileName(title, extension));
    }

    [Fact]
    public void Characters_Windows_rejects_are_stripped_from_the_suggested_name()
    {
        string suggested = Strings.SuggestFileName("أرشيف/2026: نسخة*1?", ".docx");

        Assert.DoesNotContain('/', suggested);
        Assert.DoesNotContain(':', suggested);
        Assert.DoesNotContain('*', suggested);
        Assert.DoesNotContain('?', suggested);
        Assert.EndsWith(".docx", suggested, StringComparison.Ordinal);
    }

    [Fact]
    public void A_very_long_folder_name_is_shortened_to_a_usable_file_name()
    {
        string suggested = Strings.SuggestFileName(new string('م', 400), ".xlsx");

        Assert.True(suggested.Length <= 130, "the suggested name must stay within what Windows accepts");
        Assert.EndsWith(".xlsx", suggested, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_title_still_yields_a_usable_file_name()
    {
        Assert.Equal("قائمة الملفات وعدد الصفحات.docx", Strings.SuggestFileName("   ", ".docx"));
    }
}
