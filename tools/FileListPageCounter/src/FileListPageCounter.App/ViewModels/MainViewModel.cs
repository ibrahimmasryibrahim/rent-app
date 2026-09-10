using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Threading;
using FileListPageCounter.App.Infrastructure;
using FileListPageCounter.App.Printing;
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

/// <summary>One entry of the rows-per-page list: a number, or "تلقائي", or "عدد مخصص".</summary>
public sealed class RowsPerPageOption
{
    public RowsPerPageOption(int rows, string label, bool isCustom = false)
    {
        Rows = rows;
        Label = label;
        IsCustom = isCustom;
    }

    /// <summary>Zero means "as many as fit".</summary>
    public int Rows { get; }

    public string Label { get; }

    public bool IsCustom { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Drives the main window.
///
/// The rows shown in the table are a working copy: they start as a projection of what was read
/// off the disk, and from that moment they belong to the user. Editing them can no more reach a
/// source file than editing a printout could — the report writers only ever see these rows.
///
/// Nothing is written anywhere until the user asks for it by name: no settings file, no report,
/// no temporary file. Closing the window discards the working copy and leaves the disk as it was.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>Drawing every page of a huge list would stall the window; the preview shows the start.</summary>
    private const int PreviewPageLimit = 25;

    private readonly IDialogService _dialogs;
    private readonly ScanService _scanService = new();
    private readonly DispatcherTimer _previewDebounce;

    private IReadOnlyList<FileEntry> _scanned = Array.Empty<FileEntry>();
    private string? _sourceFolder;
    private IReadOnlyList<string>? _selectedFiles;
    private CancellationTokenSource? _cancellation;
    private ProcessingLog? _lastLog;

    // Scan options, held for this session only.
    private bool _includeSubdirectories = true;
    private bool _ignoreUnsupportedFiles = true;
    private bool _countTiffFrames = true;
    private bool _verifyIntegrity = true;
    private bool _useFolderNameAsTitle = true;
    private SortMode _sortMode = SortMode.ByFileName;

    // Report options, all of which drive the live preview.
    private string _reportTitle = Strings.ReportTitle;
    private int _fontSize = ReportOptions.DefaultFontSize;
    private int _columnBlocks = 1;
    private int _rowsPerPage;

    // The name goes at the foot of the page by default; it is a switch, not a chore to set up.
    private string _userName = ReportOptions.DefaultUserName;
    private bool _showUserName = true;

    // Which blocks of the page are printed. All on to begin with; the print window turns them
    // off one by one against a live preview.
    private bool _showTitle = true;
    private bool _showDateLine = true;
    private bool _showTotalsBand = true;
    private bool _showRunningHeader = true;
    private bool _showSummary = true;
    private bool _includePageNumbers = true;

    private string _sourceDescription = "لم يتم اختيار مصدر بعد";
    private string _statusText = "جاهز";
    private string _progressText = string.Empty;
    private double _progressValue;
    private bool _isBusy;
    private int _logEntryCount;
    private bool _showProcessingStatus = true;
    private FixedDocument? _previewDocument;
    private string _previewSummary = string.Empty;
    private RowsPerPageOption _selectedRowsPerPage;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        RowsPerPageOptions = BuildRowsPerPageOptions();
        _selectedRowsPerPage = RowsPerPageOptions[0];

        Rows.CollectionChanged += OnRowsChanged;

        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            RefreshPreview();
        };

        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync, () => !IsBusy);
        SelectFilesCommand = new AsyncRelayCommand(SelectFilesAsync, () => !IsBusy);

        SaveWordCommand = new RelayCommand(SaveWord, CanProduce);
        SaveExcelCommand = new RelayCommand(SaveExcel, CanProduce);
        PrintCommand = new RelayCommand(Print, CanProduce);

        AddRowCommand = new RelayCommand(AddRow, () => !IsBusy);
        DeleteRowsCommand = new RelayCommand(DeleteRows, () => !IsBusy && SelectedRows.Count > 0);
        MoveUpCommand = new RelayCommand(MoveUp, () => !IsBusy && SelectedRows.Count > 0);
        MoveDownCommand = new RelayCommand(MoveDown, () => !IsBusy && SelectedRows.Count > 0);
        RenumberCommand = new RelayCommand(Renumber, () => !IsBusy && Rows.Count > 0);

        ToggleProcessingStatusCommand = new RelayCommand(() => ShowProcessingStatus = !ShowProcessingStatus);
        ClearCommand = new RelayCommand(Clear, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        SaveLogCommand = new RelayCommand(SaveLog, () => HasLogEntries);
    }

    // ------------------------------------------------------------- commands

    public AsyncRelayCommand SelectFolderCommand { get; }

    public AsyncRelayCommand SelectFilesCommand { get; }

    public RelayCommand SaveWordCommand { get; }

    public RelayCommand SaveExcelCommand { get; }

    public RelayCommand PrintCommand { get; }

    public RelayCommand AddRowCommand { get; }

    public RelayCommand DeleteRowsCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public RelayCommand RenumberCommand { get; }

    public RelayCommand ToggleProcessingStatusCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SaveLogCommand { get; }

    private bool CanProduce() => !IsBusy && Rows.Count > 0;

    // ------------------------------------------------------------- the rows

    /// <summary>The working copy: everything the user sees, edits, reorders and prints.</summary>
    public ObservableCollection<ReportRow> Rows { get; } = new();

    /// <summary>Kept in step with the grid by the window, because DataGrid.SelectedItems is not bindable.</summary>
    public IList<ReportRow> SelectedRows { get; } = new List<ReportRow>();

    public void OnSelectionChanged(IEnumerable<ReportRow> selection)
    {
        SelectedRows.Clear();
        foreach (ReportRow row in selection) SelectedRows.Add(row);

        DeleteRowsCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ReportRow row in e.OldItems.OfType<ReportRow>())
            {
                row.PropertyChanged -= OnRowEdited;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ReportRow row in e.NewItems.OfType<ReportRow>())
            {
                row.PropertyChanged += OnRowEdited;
            }
        }

        OnTableChanged();
    }

    private void OnRowEdited(object? sender, PropertyChangedEventArgs e) => OnTableChanged();

    /// <summary>Anything that changes the table changes the totals and the preview with it.</summary>
    private void OnTableChanged()
    {
        OnPropertyChanged(nameof(TotalFiles));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(UnknownCount));
        OnPropertyChanged(nameof(ExportHint));

        SaveWordCommand.RaiseCanExecuteChanged();
        SaveExcelCommand.RaiseCanExecuteChanged();
        PrintCommand.RaiseCanExecuteChanged();
        RenumberCommand.RaiseCanExecuteChanged();

        SchedulePreview();
    }

    // ---------------------------------------------------------------- totals

    private ReportTotals Totals => ReportTotals.From(Rows);

    public int TotalFiles => Rows.Count;

    public long TotalPages => Totals.Pages;

    public int UnknownCount => Totals.Unknown;

    public string ExportHint => Rows.Count == 0
        ? "اختر مجلدًا أو ملفات أولًا"
        : $"{Num(Rows.Count)} صفًا • {Num(TotalPages)} صفحة";

    // ------------------------------------------------------------ bindables

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
            SaveWordCommand.RaiseCanExecuteChanged();
            SaveExcelCommand.RaiseCanExecuteChanged();
            PrintCommand.RaiseCanExecuteChanged();
            AddRowCommand.RaiseCanExecuteChanged();
            DeleteRowsCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged();
            MoveDownCommand.RaiseCanExecuteChanged();
            RenumberCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsBusy;

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

    /// <summary>Whether the "تمت معالجة س من ص" line and the status text are on show.</summary>
    public bool ShowProcessingStatus
    {
        get => _showProcessingStatus;
        set
        {
            if (SetProperty(ref _showProcessingStatus, value)) OnPropertyChanged(nameof(ProcessingStatusToggleText));
        }
    }

    public string ProcessingStatusToggleText => ShowProcessingStatus ? "إخفاء حالة المعالجة" : "إظهار حالة المعالجة";

    public static string Developer => Strings.Developer;

    // ------------------------------------------------------- report options

    public string ReportTitle
    {
        get => _reportTitle;
        set
        {
            if (SetProperty(ref _reportTitle, value)) SchedulePreview();
        }
    }

    public IReadOnlyList<int> FontSizes => ReportOptions.AllowedFontSizes;

    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, value)) SchedulePreview();
        }
    }

    public IReadOnlyList<int> ColumnBlockChoices { get; } = new[] { 1, 2, 3 };

    public int ColumnBlocks
    {
        get => _columnBlocks;
        set
        {
            if (SetProperty(ref _columnBlocks, ReportLayout.NormalizeBlocks(value))) SchedulePreview();
        }
    }

    public IReadOnlyList<RowsPerPageOption> RowsPerPageOptions { get; }

    public RowsPerPageOption SelectedRowsPerPage
    {
        get => _selectedRowsPerPage;
        set
        {
            if (value is null) return;

            if (value.IsCustom)
            {
                int? custom = _dialogs.AskForNumber(
                    "عدد الصفوف في الصفحة",
                    "اكتب عدد الصفوف التي تريدها في كل صفحة:",
                    _rowsPerPage > 0 ? _rowsPerPage : 25,
                    1,
                    500);

                if (custom is null)
                {
                    // Cancelled: leave the list showing whatever was chosen before.
                    OnPropertyChanged();
                    return;
                }

                RowsPerPage = custom.Value;
                SetProperty(ref _selectedRowsPerPage, OptionFor(custom.Value));
                return;
            }

            SetProperty(ref _selectedRowsPerPage, value);
            RowsPerPage = value.Rows;
        }
    }

    public int RowsPerPage
    {
        get => _rowsPerPage;
        private set
        {
            if (SetProperty(ref _rowsPerPage, value)) SchedulePreview();
        }
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (!SetProperty(ref _userName, value)) return;

            // Typing a name is itself the request to show it. Leaving the box disabled until a
            // checkbox was ticked meant a user could type nothing and see nothing, with no clue
            // which of the two was missing.
            if (!string.IsNullOrWhiteSpace(value)) ShowUserName = true;

            SchedulePreview();
        }
    }

    public bool ShowUserName
    {
        get => _showUserName;
        set
        {
            if (SetProperty(ref _showUserName, value)) SchedulePreview();
        }
    }

    // ---- page sections ----------------------------------------------------
    // Each of these is a checkbox in the print window. They live here rather than there so the
    // main preview and the Word and Excel files agree with what was chosen for the printer.

    public bool ShowTitle
    {
        get => _showTitle;
        set { if (SetProperty(ref _showTitle, value)) SchedulePreview(); }
    }

    public bool ShowDateLine
    {
        get => _showDateLine;
        set { if (SetProperty(ref _showDateLine, value)) SchedulePreview(); }
    }

    public bool ShowTotalsBand
    {
        get => _showTotalsBand;
        set { if (SetProperty(ref _showTotalsBand, value)) SchedulePreview(); }
    }

    public bool ShowRunningHeader
    {
        get => _showRunningHeader;
        set { if (SetProperty(ref _showRunningHeader, value)) SchedulePreview(); }
    }

    public bool ShowSummary
    {
        get => _showSummary;
        set { if (SetProperty(ref _showSummary, value)) SchedulePreview(); }
    }

    public bool IncludePageNumbers
    {
        get => _includePageNumbers;
        set { if (SetProperty(ref _includePageNumbers, value)) SchedulePreview(); }
    }

    private ReportOptions BuildReportOptions() => new()
    {
        Title = ReportTitle,
        FontSize = FontSize,
        ColumnBlocks = ColumnBlocks,
        RowsPerPage = RowsPerPage,
        UserName = UserName,
        ShowUserName = ShowUserName,
        ShowTitle = ShowTitle,
        ShowDateLine = ShowDateLine,
        ShowTotalsBand = ShowTotalsBand,
        ShowRunningHeader = ShowRunningHeader,
        ShowSummary = ShowSummary,
        IncludePageNumbers = IncludePageNumbers
    };

    /// <summary>Takes back the section switches the user changed in the print window.</summary>
    private void AdoptSections(ReportOptions options)
    {
        ShowTitle = options.ShowTitle;
        ShowDateLine = options.ShowDateLine;
        ShowTotalsBand = options.ShowTotalsBand;
        ShowRunningHeader = options.ShowRunningHeader;
        ShowSummary = options.ShowSummary;
        IncludePageNumbers = options.IncludePageNumbers;
        ShowUserName = options.ShowUserName;
    }

    private RowsPerPageOption OptionFor(int rows) =>
        RowsPerPageOptions.FirstOrDefault(o => !o.IsCustom && o.Rows == rows)
        ?? new RowsPerPageOption(rows, $"{rows} صفًا");

    private IReadOnlyList<RowsPerPageOption> BuildRowsPerPageOptions()
    {
        var options = new List<RowsPerPageOption>
        {
            new(0, "تلقائي (حسب ارتفاع الصفحة)")
        };

        options.AddRange(ReportOptions.SuggestedRowsPerPage.Select(n => new RowsPerPageOption(n, $"{n} صفًا")));
        options.Add(new RowsPerPageOption(0, "عدد مخصص…", isCustom: true));

        return options;
    }

    // --------------------------------------------------------------- preview

    public FixedDocument? PreviewDocument
    {
        get => _previewDocument;
        private set => SetProperty(ref _previewDocument, value);
    }

    public string PreviewSummary
    {
        get => _previewSummary;
        private set => SetProperty(ref _previewSummary, value);
    }

    private void SchedulePreview()
    {
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void RefreshPreview()
    {
        if (Rows.Count == 0)
        {
            PreviewDocument = null;
            PreviewSummary = string.Empty;
            return;
        }

        try
        {
            ReportOptions options = BuildReportOptions();
            ReportRow[] snapshot = Rows.ToArray();

            int pages = ReportLayout.EstimatePages(snapshot.Length, options.FontSize, options.ColumnBlocks, options.RowsPerPage);

            PreviewDocument = ReportPageRenderer.Render(snapshot, options, PreviewPageLimit);

            PreviewSummary = pages > PreviewPageLimit
                ? $"{Num(pages)} صفحة — تُعرض أول {PreviewPageLimit} صفحة"
                : $"{Num(pages)} صفحة";
        }
        catch (Exception ex)
        {
            PreviewDocument = null;
            PreviewSummary = "تعذر رسم المعاينة: " + ex.Message;
        }
    }

    // ------------------------------------------------------------ row edits

    private void AddRow()
    {
        int at = SelectedRows.Count > 0 ? Rows.IndexOf(SelectedRows[^1]) + 1 : Rows.Count;
        if (at < 0 || at > Rows.Count) at = Rows.Count;

        // A hand-added row has no file behind it; its page count starts undetermined.
        Rows.Insert(at, new ReportRow(at + 1, string.Empty, null));
        Renumber();
    }

    private void DeleteRows()
    {
        foreach (ReportRow row in SelectedRows.ToArray())
        {
            Rows.Remove(row);
        }

        SelectedRows.Clear();
        Renumber();
    }

    private void MoveUp() => Move(-1);

    private void MoveDown() => Move(1);

    private void Move(int direction)
    {
        List<int> positions = SelectedRows
            .Select(Rows.IndexOf)
            .Where(i => i >= 0)
            .OrderBy(i => direction < 0 ? i : -i)
            .ToList();

        if (positions.Count == 0) return;

        foreach (int from in positions)
        {
            int to = from + direction;
            if (to < 0 || to >= Rows.Count) return; // the block has hit the end; leave it alone

            Rows.Move(from, to);
        }

        Renumber();
    }

    /// <summary>Puts the first column back in order after a move, an insert or a delete.</summary>
    private void Renumber()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].Index = i + 1;
        }

        OnTableChanged();
    }

    // --------------------------------------------------------------- actions

    private async Task SelectFolderAsync()
    {
        string? folder = _dialogs.PickFolder();
        if (folder is null) return;

        _sourceFolder = folder;
        _selectedFiles = null;
        SourceDescription = folder;

        if (_useFolderNameAsTitle)
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
        SourceDescription = files.Count == 1 ? files[0] : $"{Num(files.Count)} ملفات محددة";

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

            _scanned = result.Entries;
            _lastLog = result.Log;
            LogEntryCount = result.Log.Count;

            RebuildRows();

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

    /// <summary>
    /// Builds a fresh working copy from the scan. Any hand edits are replaced, which is why this
    /// only ever runs after a scan or a filter change — never behind the user's back.
    /// </summary>
    private void RebuildRows()
    {
        var organizeOptions = new ScanOptions
        {
            IgnoreUnsupportedFiles = IgnoreUnsupportedFiles,
            SortMode = _sortMode
        };

        List<FileEntry> view = EntryOrganizer.Organize(_scanned, organizeOptions);

        Rows.CollectionChanged -= OnRowsChanged;
        foreach (ReportRow row in Rows) row.PropertyChanged -= OnRowEdited;

        Rows.Clear();
        foreach (FileEntry entry in view)
        {
            var row = ReportRow.From(entry);
            row.PropertyChanged += OnRowEdited;
            Rows.Add(row);
        }

        Rows.CollectionChanged += OnRowsChanged;
        SelectedRows.Clear();
        OnTableChanged();
    }

    // ---------------------------------------------------------------- output

    private void SaveWord() => Save(
        formatName: "Word",
        extension: ".docx",
        filterLabel: "مستند Word",
        build: static (path, rows, options) => WordReportBuilder.Build(path, rows, options));

    private void SaveExcel() => Save(
        formatName: "Excel",
        extension: ".xlsx",
        filterLabel: "مصنّف Excel",
        build: static (path, rows, options) => ExcelReportBuilder.Build(path, rows, options));

    private void Save(
        string formatName,
        string extension,
        string filterLabel,
        Action<string, IReadOnlyList<ReportRow>, ReportOptions> build)
    {
        if (Rows.Count == 0) return;

        ReportOptions options = BuildReportOptions();

        // Nothing is written until the user names a place for it.
        string? target = _dialogs.PickSaveLocation(
            Strings.SuggestFileName(options.Title, extension),
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
            build(target, Rows.ToArray(), options);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"تعذر إنشاء ملف {formatName}:\n\n" + ex.Message, "خطأ");
            return;
        }

        StatusText = "تم إنشاء الملف: " + target;

        if (_dialogs.Confirm(
                $"تم إنشاء ملف {formatName} بنجاح.\n\n" +
                $"عدد الصفوف: {Num(Rows.Count)}\n" +
                $"إجمالي الصفحات: {Num(TotalPages)}\n\n" +
                target + "\n\nهل تريد فتحه الآن؟",
                "تم الإنشاء"))
        {
            OpenDocument(target);
        }
    }

    private void Print()
    {
        if (Rows.Count == 0) return;

        try
        {
            // The window renders the pages itself, so its switches can redraw them as they are
            // ticked; whatever the user settles on comes back and becomes the app's own setting.
            ReportOptions chosen = _dialogs.ShowPrintPreview(Rows.ToArray(), BuildReportOptions());

            AdoptSections(chosen);
            StatusText = "تمت معاينة الطباعة";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("تعذر تجهيز الطباعة:\n\n" + ex.Message, "خطأ");
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
            return Path.GetFullPath(target).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // -------------------------------------------------------------- options

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set
        {
            if (!SetProperty(ref _includeSubdirectories, value)) return;

            if (_sourceFolder is not null && !IsBusy) _ = RescanAsync();
        }
    }

    public bool IgnoreUnsupportedFiles
    {
        get => _ignoreUnsupportedFiles;
        set
        {
            if (SetProperty(ref _ignoreUnsupportedFiles, value)) RebuildRows();
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

            ReportTitle = value && _sourceFolder is not null ? FolderTitle(_sourceFolder) : Strings.ReportTitle;
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
            RebuildRows();
        }
    }

    private bool HasSource => _sourceFolder is not null || _selectedFiles is { Count: > 0 };

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

    private void Clear()
    {
        _cancellation?.Cancel();
        _sourceFolder = null;
        _selectedFiles = null;
        _scanned = Array.Empty<FileEntry>();
        _lastLog = null;

        RebuildRows();

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
            string path = _lastLog.Save();
            _dialogs.ShowInfo("تم حفظ سجل التفاصيل في:\n\n" + path, "سجل المعالجة");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("تعذر حفظ السجل:\n\n" + ex.Message, "خطأ");
        }
    }

    private static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
