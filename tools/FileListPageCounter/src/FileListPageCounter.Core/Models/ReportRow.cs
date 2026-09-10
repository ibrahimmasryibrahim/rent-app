using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FileListPageCounter.Core.Common;

namespace FileListPageCounter.Core.Models;

/// <summary>
/// One editable line of the report.
///
/// This is deliberately a different type from <see cref="FileEntry"/>. A FileEntry is what was
/// read off the disk and is never changed; a ReportRow is what the user is about to print, and
/// every field on it can be typed over. Because the report writers only ever see ReportRows,
/// there is no code path at all by which editing the table could reach a source file — the
/// read-only promise is enforced by the type system rather than by discipline.
///
/// A row may also have no file behind it at all: the user can add blank lines by hand.
/// </summary>
public sealed class ReportRow : INotifyPropertyChanged
{
    private int _index;
    private string _name = string.Empty;
    private int? _pageCount;

    public ReportRow()
    {
    }

    public ReportRow(int index, string name, int? pageCount, string? sourcePath = null)
    {
        _index = index;
        _name = name ?? string.Empty;
        _pageCount = Normalize(pageCount);
        SourcePath = sourcePath;
    }

    /// <summary>Projects a scanned file into an editable row. The entry itself is left untouched.</summary>
    public static ReportRow From(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ReportRow(entry.Index, entry.DisplayName, entry.PageCount, entry.FullPath);
    }

    public static List<ReportRow> From(IEnumerable<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(From).ToList();
    }

    /// <summary>The number printed in the first column. The user may type over it.</summary>
    public int Index
    {
        get => _index;
        set => Set(ref _index, value < 0 ? 0 : value);
    }

    /// <summary>The name printed in the report — starts as the file name without its extension.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>Null means the count could not be determined, and prints as "غير معروف".</summary>
    public int? PageCount
    {
        get => _pageCount;
        set
        {
            if (Set(ref _pageCount, Normalize(value)))
            {
                OnPropertyChanged(nameof(PageCountText));
            }
        }
    }

    /// <summary>
    /// The page count as the table shows and accepts it. Typing a number sets the count; clearing
    /// the cell, or typing "غير معروف", marks it undetermined again.
    /// </summary>
    public string PageCountText
    {
        get => _pageCount.HasValue
            ? _pageCount.Value.ToString(CultureInfo.InvariantCulture)
            : Strings.Unknown;
        set
        {
            string text = (value ?? string.Empty).Trim();

            if (text.Length == 0 || string.Equals(text, Strings.Unknown, StringComparison.Ordinal))
            {
                PageCount = null;
                return;
            }

            // Accept Arabic-Indic digits as readily as Western ones.
            string western = ToWesternDigits(text);

            PageCount = int.TryParse(western, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;
        }
    }

    /// <summary>
    /// Where the row came from, kept only so the preview can show it. Nothing ever writes here,
    /// and a row the user added by hand has none.
    /// </summary>
    public string? SourcePath { get; init; }

    public bool HasSource => !string.IsNullOrEmpty(SourcePath);

    public ReportRow Clone() => new(_index, _name, _pageCount, SourcePath);

    private static int? Normalize(int? value) => value is null or < 0 ? null : value;

    /// <summary>٠١٢٣٤٥٦٧٨٩ and ۰۱۲۳۴۵۶۷۸۹ both mean the same digits as 0123456789.</summary>
    private static string ToWesternDigits(string text)
    {
        Span<char> buffer = stackalloc char[text.Length];

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            buffer[i] = c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // Arabic-Indic
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // Extended Arabic-Indic
                _ => c
            };
        }

        return new string(buffer);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
