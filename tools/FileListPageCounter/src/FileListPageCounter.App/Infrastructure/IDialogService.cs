namespace FileListPageCounter.App.Infrastructure;

public interface IDialogService
{
    string? PickFolder();

    IReadOnlyList<string>? PickFiles(IReadOnlyCollection<string> supportedExtensions);

    string? PickSaveLocation(string defaultFileName, string extension, string filterLabel);

    /// <summary>Asks for a whole number within a range. Null when the user cancels.</summary>
    int? AskForNumber(string title, string prompt, int current, int minimum, int maximum);

    /// <summary>Opens the print preview on the pages that were rendered.</summary>
    void ShowPrintPreview(System.Windows.Documents.FixedDocument document, string title);

    void ShowInfo(string message, string title);

    void ShowWarning(string message, string title);

    void ShowError(string message, string title);

    bool Confirm(string message, string title);
}
