using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;   // PrintDialog lives here, not in System.Windows
using System.Windows.Documents;

namespace FileListPageCounter.App;

/// <summary>
/// Shows the very pages that will be printed, and prints them. The document handed in here is
/// the same one the live preview draws, so there is no gap between what was reviewed and what
/// comes out of the printer.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    private readonly FixedDocument _document;
    private readonly string _title;

    public PrintPreviewWindow(FixedDocument document, string title)
    {
        _document = document;
        _title = title;

        InitializeComponent();

        Viewer.Document = document;
        PageCountText.Text = $"{document.Pages.Count.ToString("N0", CultureInfo.InvariantCulture)} صفحة";
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            // Ask the driver for A4 portrait; a printer that cannot do it keeps its own default
            // rather than failing the job.
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            dialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
        }
        catch (Exception)
        {
            // Not every driver exposes these; printing still proceeds.
        }

        try
        {
            dialog.PrintDocument(_document.DocumentPaginator, _title);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "تعذر إرسال المستند إلى الطابعة:\n\n" + ex.Message,
                "خطأ في الطباعة",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
