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

    public string? PickSaveLocation(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "حفظ ملف Word",
            FileName = defaultFileName,
            DefaultExt = ".docx",
            AddExtension = true,
            OverwritePrompt = true,
            Filter = "مستند Word (*.docx)|*.docx"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
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
