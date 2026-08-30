using System.Globalization;
using FileListPageCounter.Core.Common;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>Requirement 16: natural ordering — 1, 2, 3, 10, 11, 20 — not lexicographic.</summary>
public class NaturalSortTests
{
    private static readonly NaturalComparer Comparer = new(CultureInfo.InvariantCulture);

    [Fact]
    public void Numbers_sort_by_value_not_by_text()
    {
        string[] input = { "20", "3", "1", "11", "10", "2" };
        string[] expected = { "1", "2", "3", "10", "11", "20" };

        Assert.Equal(expected, input.OrderBy(x => x, Comparer).ToArray());
    }

    [Fact]
    public void Mixed_names_sort_naturally()
    {
        string[] input = { "ملف 10", "ملف 2", "ملف 1", "ملف 20", "ملف 3" };
        string[] expected = { "ملف 1", "ملف 2", "ملف 3", "ملف 10", "ملف 20" };

        Assert.Equal(expected, input.OrderBy(x => x, Comparer).ToArray());
    }

    [Fact]
    public void Leading_zeros_do_not_change_the_numeric_value()
    {
        Assert.True(Comparer.Compare("007", "10") < 0);
        Assert.True(Comparer.Compare("0010", "9") > 0);
    }

    [Fact]
    public void Very_long_numbers_are_compared_without_overflow()
    {
        Assert.True(Comparer.Compare("99999999999999999999", "100000000000000000000") < 0);
    }

    [Fact]
    public void Comparison_is_a_total_order()
    {
        Assert.Equal(0, Comparer.Compare("Doc-1", "Doc-1"));
        Assert.True(Comparer.Compare("Doc-1", "Doc-2") < 0);
        Assert.True(Comparer.Compare("Doc-2", "Doc-1") > 0);
    }
}
