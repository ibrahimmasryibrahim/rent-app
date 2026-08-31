using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
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

public sealed class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ScanService _scanService = new();
    private readonly AppSettings _settings;

    /// <summary>Everything the last scan found, unfiltered — lets option changes re-apply instantly.</summary>
    private IReadOnlyList<FileEntry> _rawEntries = Array.Empty<FileEntry>();

    private string? _sourceFolder;
    private IReadOnlyList<string>? _selectedFiles;
    private CancellationTokenSource? _cancellation;

    private IReadOnlyList<FileEntry> _entries = Array.Empty<FileEntry>();
    private string _sourceDescription = "لم يتم اختيار مصدر بعد";
    private string _statusText = "جاهز";
    private string _progressText = string.Empty;
    private double _progressValue;
    private bool _isBusy;
    private int _totalFiles;
    private int _totalPages;
    private int _unknownCount;
    private string? _lastLogPath;
    private int _logEntryCount;
    private ProcessingLog? _lastLog;
    private string _reportTitle = Strings.ReportTitle;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _settings = AppSettings.Load();

        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync, () => !IsBusy);
        SelectFilesCommand = new AsyncRelayCommand(SelectFilesAsync, () => !IsBusy);
        CreateWordCommand = new RelayCommand(CreateWord, () => !IsBusy && Entries.Count > 0);
        CreateExcelCommand = new RelayCommand(CreateExcel, () => !IsBusy && Entries.Count > 0);
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
            if (SetProperty(ref _logEntryCount, value))
            {
                OnPropertyChanged(nameof(HasLogEntries));
                SaveLogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasLogEntries => LogEntryCount > 0;

    // -------------------------------------------------------------- options

    public bool IncludeSubdirectories
    {
        get => _settings.IncludeSubdirectories;
        set
        {
            if (_settings.IncludeSubdirectories == value) return;

            _settings.IncludeSubdirectories = value;
            _settings.Save();
            OnPropertyChanged();

            // Only a folder scan is affected, and it needs the disk again.
            if (_sourceFolder is not null && !IsBusy) _ = RescanAsync();
        }
    }

    public bool IgnoreUnsupportedFiles
    {
        get => _settings.IgnoreUnsupportedFiles;
        set
        {
            if (_settings.IgnoreUnsupportedFiles == value) return;

            _settings.IgnoreUnsupportedFiles = value;
            _settings.Save();
            OnPropertyChanged();
            ReapplyView();
        }
    }

    public bool CountTiffFrames
    {
        get => _settings.CountTiffFrames;
        set
        {
            if (_settings.CountTiffFrames == value) return;

            _settings.CountTiffFrames = value;
            _settings.Save();
            OnPropertyChanged();

            if (HasSource && !IsBusy) _ = RescanAsync();
        }
    }

    public bool VerifyIntegrity
    {
        get => _settings.VerifyIntegrity;
        set
        {
            if (_settings.VerifyIntegrity == value) return;

            _settings.VerifyIntegrity = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool OpenAfterCreate
    {
        get => _settings.OpenAfterCreate;
        set
        {
            if (_settings.OpenAfterCreate == value) return;

            _settings.OpenAfterCreate = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool UseFolderNameAsTitle
    {
        get => _settings.UseFolderNameAsTitle;
        set
        {
            if (_settings.UseFolderNameAsTitle == value) return;

            _settings.UseFolderNameAsTitle = value;
            _settings.Save();
            OnPropertyChanged();

            ReportTitle = value && _sourceFolder is not null
                ? FolderTitle(_sourceFolder)
                : Strings.ReportTitle;
        }
    }

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

    public IReadOnlyList<int> FontSizes => ReportOptions.AllowedFontSizes;

    public int FontSize
    {
        get => _settings.FontSize;
        set
        {
            if (_settings.FontSize == value) return;

            _settings.FontSize = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<SortOptionItem> SortOptions { get; } = new[]
    {
        new SortOptionItem(SortMode.ByFileName, "حسب اسم الملف"),
        new SortOptionItem(SortMode.FolderOrder, "حسب ترتيب الملفات في المجلد")
    };

    public SortOptionItem SelectedSortOption
    {
        get => SortOptions.FirstOrDefault(o => o.Mode == _settings.SortMode) ?? SortOptions[0];
        set
        {
            if (value is null || _settings.SortMode == value.Mode) return;

            _settings.SortMode = value.Mode;
            _settings.Save();
            OnPropertyChanged();
            ReapplyView();
        }
    }

    private bool HasSource => _sourceFolder is not null || _selectedFiles is { Count: > 0 };

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
            SortMode = _settings.SortMode
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
            _lastLogPath = null;
            LogEntryCount = result.Log.Count;
            _lastLog = result.Log;

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
            SortMode = _settings.SortMode
        };

        List<FileEntry> view = EntryOrganizer.Organize(_rawEntries, options);

        Entries = view;
        TotalFiles = view.Count;
        TotalPages = view.Sum(e => e.PageCount ?? 0);
        UnknownCount = view.Count(e => !e.PageCount.HasValue);
    }

    private void CreateWord() => CreateReport(
        ".docx",
        "مستند Word",
        "Word",
        static (path, entries, options) => WordReportBuilder.Build(path, entries, options));

    private void CreateExcel() => CreateReport(
        ".xlsx",
        "مصنّف Excel",
        "Excel",
        static (path, entries, options) => ExcelReportBuilder.Build(path, entries, options));

    private void CreateReport(
        string extension,
        string filterLabel,
        string formatName,
        Action<string, IReadOnlyList<FileEntry>, ReportOptions> build)
    {
        if (Entries.Count == 0) return;

        var reportOptions = new ReportOptions
        {
            FontSize = FontSize,
            Title = ReportTitle
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

        string summary =
            "تم إنشاء الملف بنجاح.\n\n" +
            $"عدد الملفات: {Num(TotalFiles)}\n" +
            $"إجمالي الصفحات: {Num(TotalPages)}\n" +
            $"تعذر تحديد صفحاتها: {Num(UnknownCount)}\n\n" +
            target;

        if (OpenAfterCreate)
        {
            if (_dialogs.Confirm(summary + "\n\nهل تريد فتح الملف الآن؟", "تم الإنشاء"))
            {
                OpenDocument(target);
            }
        }
        else
        {
            _dialogs.ShowInfo(summary, "تم الإنشاء");
        }
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
        _lastLogPath = null;

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
    }

    private void Cancel() => _cancellation?.Cancel();

    private void SaveLog()
    {
        if (_lastLog is null || _lastLog.Count == 0) return;

        try
        {
            _lastLogPath = _lastLog.Save();
            _dialogs.ShowInfo("تم حفظ سجل التفاصيل في:\n\n" + _lastLogPath, "سجل المعالجة");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("تعذر حفظ السجل:\n\n" + ex.Message, "خطأ");
        }
    }

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
