namespace FileListPageCounter.App.Infrastructure;

public interface IDialogService
{
    string? PickFolder();

    IReadOnlyList<string>? PickFiles(IReadOnlyCollection<string> supportedExtensions);

    string? PickSaveLocation(string defaultFileName, string extension, string filterLabel);

    /// <summary>Asks for a whole number within a range. Null when the user cancels.</summary>
    int? AskForNumber(string title, string prompt, int current, int minimum, int maximum);

    /// <summary>
    /// Opens the print preview on these rows. The window draws the pages itself so its switches
    /// can redraw them live; it returns the options as the user left them, which the caller keeps
    /// so the same choices apply to the main preview and to Word and Excel.
    /// </summary>
    Core.Models.ReportOptions ShowPrintPreview(
        IReadOnlyList<Core.Models.ReportRow> rows,
        Core.Models.ReportOptions options);

    void ShowInfo(string message, string title);

    void ShowWarning(string message, string title);

    void ShowError(string message, string title);

    bool Confirm(string message, string title);
}
