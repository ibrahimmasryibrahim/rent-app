using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FileListPageCounter.App.Infrastructure;
using FileListPageCounter.App.ViewModels;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new WindowsDialogService());
        DataContext = _viewModel;

        // The version belongs in the title bar: it is the one place a user can always check
        // which build they are actually running.
        Title = $"File List & Page Counter  v{AppVersion()}  —  استخراج أسماء الملفات وعدد الصفحات";
    }

    /// <summary>DataGrid.SelectedItems is not a bindable property, so the window relays it.</summary>
    private void OnRowsGridSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.OnSelectionChanged(RowsGrid.SelectedItems.OfType<ReportRow>());

    private static string AppVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0" : $"{version.Major}.{version.Minor}";
    }
}
