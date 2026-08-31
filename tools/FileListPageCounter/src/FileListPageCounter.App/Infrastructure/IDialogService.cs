namespace FileListPageCounter.App.Infrastructure;

public interface IDialogService
{
    string? PickFolder();

    IReadOnlyList<string>? PickFiles(IReadOnlyCollection<string> supportedExtensions);

    string? PickSaveLocation(string defaultFileName, string extension, string filterLabel);

    void ShowInfo(string message, string title);

    void ShowWarning(string message, string title);

    void ShowError(string message, string title);

    bool Confirm(string message, string title);
}
