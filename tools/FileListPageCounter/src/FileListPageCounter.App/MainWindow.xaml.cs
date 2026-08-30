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
    }
}
