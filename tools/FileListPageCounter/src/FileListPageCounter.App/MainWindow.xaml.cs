using System.Reflection;
using System.Windows;
using FileListPageCounter.App.Infrastructure;
using FileListPageCounter.App.ViewModels;

namespace FileListPageCounter.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new WindowsDialogService());

        // The version belongs in the title bar: it is the one place a user can always check
        // which build they are actually running.
        Title = $"FILE LIST & PAGE COUNTER v{AppVersion()} — استخراج أسماء الملفات وعدد الصفحات";
    }

    private static string AppVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0" : $"{version.Major}.{version.Minor}";
    }
}
