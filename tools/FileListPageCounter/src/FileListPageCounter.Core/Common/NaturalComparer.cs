using System.Globalization;

namespace FileListPageCounter.Core.Common;

/// <summary>
/// "Natural" (human) ordering: 1, 2, 3, 10, 11, 20 instead of 1, 10, 11, 2, 20, 3.
/// Digit runs are compared as numbers of unbounded length; everything else is compared
/// with the supplied culture so Arabic, Latin and mixed names all sort sensibly.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Ordinal = new(CultureInfo.InvariantCulture);

    private readonly CompareInfo _compareInfo;
    private const CompareOptions Options = CompareOptions.IgnoreCase;

    public NaturalComparer(CultureInfo culture) => _compareInfo = culture.CompareInfo;

    public NaturalComparer() : this(CultureInfo.CurrentCulture) { }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            bool xDigit = char.IsDigit(x[i]);
            bool yDigit = char.IsDigit(y[j]);

            if (xDigit && yDigit)
            {
                int xStart = i, yStart = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                int cmp = CompareNumericRun(x.AsSpan(xStart, i - xStart), y.AsSpan(yStart, j - yStart));
                if (cmp != 0) return cmp;
            }
            else if (xDigit != yDigit)
            {
                // A digit sorts before a letter so "1 file" precedes "file 1".
                return xDigit ? -1 : 1;
            }
            else
            {
                int xStart = i, yStart = j;
                while (i < x.Length && !char.IsDigit(x[i])) i++;
                while (j < y.Length && !char.IsDigit(y[j])) j++;

                int cmp = _compareInfo.Compare(x, xStart, i - xStart, y, yStart, j - yStart, Options);
                if (cmp != 0) return cmp;
            }
        }

        int remaining = (x.Length - i).CompareTo(y.Length - j);
        if (remaining != 0) return remaining;

        // Identical under the culture comparison: fall back to ordinal for a stable, total order.
        return string.CompareOrdinal(x, y);
    }

    private static int CompareNumericRun(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        ReadOnlySpan<char> at = TrimLeadingZeros(a);
        ReadOnlySpan<char> bt = TrimLeadingZeros(b);

        if (at.Length != bt.Length) return at.Length < bt.Length ? -1 : 1;

        int cmp = at.SequenceCompareTo(bt);
        if (cmp != 0) return cmp < 0 ? -1 : 1;

        // Same value: "007" after "7" keeps the order deterministic.
        return a.Length.CompareTo(b.Length);
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> s)
    {
        int k = 0;
        while (k < s.Length - 1 && s[k] == '0') k++;
        return s[k..];
    }
}
