using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace FileListPageCounter.App.Infrastructure;

/// <summary>
/// The only place the application talks to Windows shell dialogs. Files are picked by path —
/// nothing is uploaded, copied or streamed anywhere; the application simply learns where to read.
/// </summary>
public sealed class WindowsDialogService : IDialogService
{
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "اختيار مجلد",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public IReadOnlyList<string>? PickFiles(IReadOnlyCollection<string> supportedExtensions)
    {
        string pattern = string.Join(";", supportedExtensions.Select(e => "*" + e));

        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملفات",
            Multiselect = true,
            CheckFileExists = true,
            Filter = $"الملفات المدعومة ({pattern})|{pattern}|كل الملفات (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    public string? PickSaveLocation(string defaultFileName, string extension, string filterLabel)
    {
        var dialog = new SaveFileDialog
        {
            Title = "اختيار مكان الحفظ",
            FileName = defaultFileName,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = $"{filterLabel} (*{extension})|*{extension}"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public ExportChoice? RequestExportOptions(ExportRequest request)
    {
        var window = new ExportOptionsWindow(request) { Owner = Owner() };
        return window.ShowDialog() == true ? window.Choice : null;
    }

    public void ShowInfo(string message, string title) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static Window? Owner() => Application.Current?.MainWindow;
}
