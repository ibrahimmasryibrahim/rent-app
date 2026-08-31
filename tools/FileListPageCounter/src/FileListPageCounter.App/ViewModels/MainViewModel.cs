using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using FileListPageCounter.App.Infrastructure;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Diagnostics;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Core.Scanning;

namespace FileListPageCounter.App.ViewModels;

public sealed class SortOptionItem
{
    public SortOptionItem(SortMode mode, string label)
    {
        Mode = mode;
        Label = label;
    }

    public SortMode Mode { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Drives the main window. Scan options live here in memory only — nothing is written to disk,
/// so the tool leaves no configuration file anywhere and starts from the same sane defaults
/// every time. Everything that shapes a report is asked for in the export dialog instead.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ScanService _scanService = new();

    /// <summary>Everything the last scan found, unfiltered — lets option changes re-apply instantly.</summary>
    private IReadOnlyList<FileEntry> _rawEntries = Array.Empty<FileEntry>();

    private string? _sourceFolder;
    private IReadOnlyList<string>? _selectedFiles;
    private CancellationTokenSource? _cancellation;
    private ProcessingLog? _lastLog;

    // Scan options, remembered for this session only.
    private bool _includeSubdirectories = true;
    private bool _ignoreUnsupportedFiles = true;
    private bool _countTiffFrames = true;
    private bool _verifyIntegrity = true;
    private bool _useFolderNameAsTitle = true;
    private SortMode _sortMode = SortMode.ByFileName;

    // Export choices, carried from one export to the next within the session.
    private int _fontSize = ReportOptions.DefaultFontSize;
    private int _columnBlocks = 1;

    private IReadOnlyList<FileEntry> _entries = Array.Empty<FileEntry>();
    private string _sourceDescription = "لم يتم اختيار مصدر بعد";
    private string _statusText = "جاهز";
    private string _progressText = string.Empty;
    private string _reportTitle = Strings.ReportTitle;
    private double _progressValue;
    private bool _isBusy;
    private int _totalFiles;
    private int _totalPages;
    private int _unknownCount;
    private int _logEntryCount;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync, () => !IsBusy);
        SelectFilesCommand = new AsyncRelayCommand(SelectFilesAsync, () => !IsBusy);
        CreateWordCommand = new RelayCommand(CreateWord, CanExport);
        CreateExcelCommand = new RelayCommand(CreateExcel, CanExport);
        ClearCommand = new RelayCommand(Clear, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        SaveLogCommand = new RelayCommand(SaveLog, () => HasLogEntries);
    }

    // ------------------------------------------------------------- commands

    public AsyncRelayCommand SelectFolderCommand { get; }

    public AsyncRelayCommand SelectFilesCommand { get; }

    public RelayCommand CreateWordCommand { get; }

    public RelayCommand CreateExcelCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SaveLogCommand { get; }

    private bool CanExport() => !IsBusy && Entries.Count > 0;

    // ------------------------------------------------------------ bindables

    public IReadOnlyList<FileEntry> Entries
    {
        get => _entries;
        private set
        {
            if (!SetProperty(ref _entries, value)) return;

            CreateWordCommand.RaiseCanExecuteChanged();
            CreateExcelCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Heading printed at the top of the Word and Excel reports.</summary>
    public string ReportTitle
    {
        get => _reportTitle;
        set => SetProperty(ref _reportTitle, value);
    }

    /// <summary>The tool's author, shown in the window and stamped into every report.</summary>
    public static string Developer => Strings.Developer;

    /// <summary>Says what the export buttons will actually produce, next to the buttons themselves.</summary>
    public string ExportHint => Entries.Count == 0
        ? "اختر مجلدًا أو ملفات أولًا لتفعيل التصدير"
        : $"سيتم تصدير {Num(Entries.Count)} ملفًا بإجمالي {Num(TotalPages)} صفحة";

    public string SourceDescription
    {
        get => _sourceDescription;
        private set => SetProperty(ref _sourceDescription, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;

            OnPropertyChanged(nameof(IsIdle));
            SelectFolderCommand.RaiseCanExecuteChanged();
            SelectFilesCommand.RaiseCanExecuteChanged();
            CreateWordCommand.RaiseCanExecuteChanged();
            CreateExcelCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsBusy;

    public int TotalFiles
    {
        get => _totalFiles;
        private set => SetProperty(ref _totalFiles, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => SetProperty(ref _totalPages, value);
    }

    public int UnknownCount
    {
        get => _unknownCount;
        private set => SetProperty(ref _unknownCount, value);
    }

    /// <summary>Notes and failures recorded during the last scan (drives the log button).</summary>
    public int LogEntryCount
    {
        get => _logEntryCount;
        private set
        {
            if (!SetProperty(ref _logEntryCount, value)) return;

            OnPropertyChanged(nameof(HasLogEntries));
            SaveLogCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasLogEntries => LogEntryCount > 0;

    // -------------------------------------------------------------- options

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set
        {
            if (!SetProperty(ref _includeSubdirectories, value)) return;

            // Only a folder scan is affected, and it needs the disk again.
            if (_sourceFolder is not null && !IsBusy) _ = RescanAsync();
        }
    }

    public bool IgnoreUnsupportedFiles
    {
        get => _ignoreUnsupportedFiles;
        set
        {
            if (SetProperty(ref _ignoreUnsupportedFiles, value)) ReapplyView();
        }
    }

    public bool CountTiffFrames
    {
        get => _countTiffFrames;
        set
        {
            if (!SetProperty(ref _countTiffFrames, value)) return;

            if (HasSource && !IsBusy) _ = RescanAsync();
        }
    }

    public bool VerifyIntegrity
    {
        get => _verifyIntegrity;
        set => SetProperty(ref _verifyIntegrity, value);
    }

    public bool UseFolderNameAsTitle
    {
        get => _useFolderNameAsTitle;
        set
        {
            if (!SetProperty(ref _useFolderNameAsTitle, value)) return;

            ReportTitle = value && _sourceFolder is not null
                ? FolderTitle(_sourceFolder)
                : Strings.ReportTitle;
        }
    }

    public IReadOnlyList<SortOptionItem> SortOptions { get; } = new[]
    {
        new SortOptionItem(SortMode.ByFileName, "حسب اسم الملف"),
        new SortOptionItem(SortMode.FolderOrder, "حسب ترتيب الملفات في المجلد")
    };

    public SortOptionItem SelectedSortOption
    {
        get => SortOptions.FirstOrDefault(o => o.Mode == _sortMode) ?? SortOptions[0];
        set
        {
            if (value is null || _sortMode == value.Mode) return;

            _sortMode = value.Mode;
            OnPropertyChanged();
            ReapplyView();
        }
    }

    private bool HasSource => _sourceFolder is not null || _selectedFiles is { Count: > 0 };

    /// <summary>
    /// The folder's own name, which is what a user means by "name the report after the folder".
    /// A drive root has no name of its own, so its path stands in for one.
    /// </summary>
    private static string FolderTitle(string folder)
    {
        try
        {
            string name = new DirectoryInfo(folder).Name;
            return string.IsNullOrWhiteSpace(name) ? folder : name;
        }
        catch (Exception)
        {
            return Strings.ReportTitle;
        }
    }

    // --------------------------------------------------------------- actions

    private async Task SelectFolderAsync()
    {
        string? folder = _dialogs.PickFolder();
        if (folder is null) return;

        _sourceFolder = folder;
        _selectedFiles = null;
        SourceDescription = folder;

        if (UseFolderNameAsTitle)
        {
            ReportTitle = FolderTitle(folder);
        }

        await RescanAsync().ConfigureAwait(true);
    }

    private async Task SelectFilesAsync()
    {
        IReadOnlyList<string>? files = _dialogs.PickFiles(_scanService.Registry.SupportedExtensions);
        if (files is null || files.Count == 0) return;

        _selectedFiles = files;
        _sourceFolder = null;
        SourceDescription = files.Count == 1
            ? files[0]
            : $"{Num(files.Count)} ملفات محددة";

        await RescanAsync().ConfigureAwait(true);
    }

    private async Task RescanAsync()
    {
        if (!HasSource) return;

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;

        IsBusy = true;
        StatusText = "جاري فحص الملفات...";
        ProgressText = string.Empty;
        ProgressValue = 0;

        // Scan without the "ignore unsupported" filter so toggling it later is instant.
        var options = new ScanOptions
        {
            IncludeSubdirectories = IncludeSubdirectories,
            IgnoreUnsupportedFiles = false,
            CountTiffFrames = CountTiffFrames,
            VerifyIntegrity = VerifyIntegrity,
            SortMode = _sortMode
        };

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressValue = p.Percent;
            ProgressText = $"تمت معالجة {Num(p.Processed)} من {Num(p.Total)}";
        });

        try
        {
            ScanResult result = _sourceFolder is not null
                ? await _scanService.ScanFolderAsync(_sourceFolder, options, progress, token).ConfigureAwait(true)
                : await _scanService.ScanFilesAsync(_selectedFiles!, options, progress, token).ConfigureAwait(true);

            _rawEntries = result.Entries;
            _lastLog = result.Log;
            LogEntryCount = result.Log.Count;

            ReapplyView();

            StatusText = $"اكتمل الفحص خلال {result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} ثانية";

            if (result.IntegrityViolations.Count > 0)
            {
                _dialogs.ShowWarning(
                    "تم رصد تغيّر في بعض الملفات الأصلية أثناء الفحص (قد يكون بسبب برنامج آخر):\n\n" +
                    string.Join("\n", result.IntegrityViolations.Take(10)),
                    "تحقق سلامة الملفات");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "تم إيقاف الفحص";
            ProgressText = string.Empty;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("تعذر إكمال الفحص:\n\n" + ex.Message, "خطأ");
            StatusText = "توقف الفحص بسبب خطأ";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-applies the filter, the sort and the row numbers without reading the disk again.</summary>
    private void ReapplyView()
    {
        var options = new ScanOptions
        {
            IgnoreUnsupportedFiles = IgnoreUnsupportedFiles,
            SortMode = _sortMode
        };

        List<FileEntry> view = EntryOrganizer.Organize(_rawEntries, options);

        Entries = view;
        TotalFiles = view.Count;
        TotalPages = view.Sum(e => e.PageCount ?? 0);
        UnknownCount = view.Count(e => !e.PageCount.HasValue);
        OnPropertyChanged(nameof(ExportHint));
    }

    // --------------------------------------------------------------- export

    private void CreateWord() => Export(
        formatName: "Word",
        extension: ".docx",
        filterLabel: "مستند Word",
        paginated: true,
        build: static (path, entries, options) => WordReportBuilder.Build(path, entries, options));

    private void CreateExcel() => Export(
        formatName: "Excel",
        extension: ".xlsx",
        filterLabel: "مصنّف Excel",
        paginated: false,
        build: static (path, entries, options) => ExcelReportBuilder.Build(path, entries, options));

    private void Export(
        string formatName,
        string extension,
        string filterLabel,
        bool paginated,
        Action<string, IReadOnlyList<FileEntry>, ReportOptions> build)
    {
        if (Entries.Count == 0) return;

        // Ask first, save second: the user shapes the document before choosing where it lands.
        ExportChoice? choice = _dialogs.RequestExportOptions(new ExportRequest
        {
            FormatName = formatName,
            Paginated = paginated,
            EntryCount = Entries.Count,
            TotalPages = TotalPages,
            Title = ReportTitle,
            FontSize = _fontSize,
            ColumnBlocks = _columnBlocks
        });

        if (choice is null) return;

        _fontSize = choice.FontSize;
        _columnBlocks = choice.ColumnBlocks;
        ReportTitle = choice.Title;

        var reportOptions = new ReportOptions
        {
            Title = choice.Title,
            FontSize = choice.FontSize,
            ColumnBlocks = choice.ColumnBlocks
        };

        string? target = _dialogs.PickSaveLocation(
            Strings.SuggestFileName(reportOptions.Title, extension),
            extension,
            filterLabel);

        if (target is null) return;

        if (IsInsideSourceFolder(target) &&
            !_dialogs.Confirm(
                "المكان المختار يقع داخل مجلد المصدر.\n" +
                "يُفضّل حفظ التقرير خارج مجلد الأرشيف حتى يبقى المجلد كما هو تمامًا.\n\n" +
                "هل تريد المتابعة؟",
                "تأكيد مكان الحفظ"))
        {
            return;
        }

        try
        {
            build(target, Entries, reportOptions);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"تعذر إنشاء ملف {formatName}:\n\n" + ex.Message, "خطأ");
            return;
        }

        StatusText = "تم إنشاء الملف: " + target;

        if (choice.OpenWhenDone)
        {
            OpenDocument(target);
            return;
        }

        _dialogs.ShowInfo(
            "تم إنشاء الملف بنجاح.\n\n" +
            $"عدد الملفات: {Num(TotalFiles)}\n" +
            $"إجمالي الصفحات: {Num(TotalPages)}\n" +
            $"تعذر تحديد صفحاتها: {Num(UnknownCount)}\n\n" +
            target,
            "تم الإنشاء");
    }

    private void OpenDocument(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("تعذر فتح الملف:\n\n" + ex.Message, "خطأ");
        }
    }

    private bool IsInsideSourceFolder(string target)
    {
        if (_sourceFolder is null) return false;

        try
        {
            string folder = Path.GetFullPath(_sourceFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string file = Path.GetFullPath(target);
            return file.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Clear()
    {
        _cancellation?.Cancel();
        _sourceFolder = null;
        _selectedFiles = null;
        _rawEntries = Array.Empty<FileEntry>();
        _lastLog = null;

        Entries = Array.Empty<FileEntry>();
        TotalFiles = 0;
        TotalPages = 0;
        UnknownCount = 0;
        LogEntryCount = 0;
        SourceDescription = "لم يتم اختيار مصدر بعد";
        StatusText = "جاهز";
        ProgressText = string.Empty;
        ProgressValue = 0;
        ReportTitle = Strings.ReportTitle;
        OnPropertyChanged(nameof(ExportHint));
    }

    private void Cancel() => _cancellation?.Cancel();

    private void SaveLog()
    {
        if (_lastLog is null || _lastLog.Count == 0) return;

        try
        {
            string path = _lastLog.Save();
            _dialogs.ShowInfo("تم حفظ سجل التفاصيل في:\n\n" + path, "سجل المعالجة");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("تعذر حفظ السجل:\n\n" + ex.Message, "خطأ");
        }
    }

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
