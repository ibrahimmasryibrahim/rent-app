using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;   // PrintDialog lives here, not in System.Windows
using System.Windows.Documents;
using System.Windows.Input;
using FileListPageCounter.App.Printing;

namespace FileListPageCounter.App;

/// <summary>
/// Shows the very pages that will be printed, and prints them. The document handed in here is
/// the same one the live preview draws, so there is no gap between what was reviewed and what
/// comes out of the printer.
///
/// Printing goes straight to the chosen printer with the settings on this panel; the driver's
/// own properties dialog is one button away for anything the panel does not cover.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    private readonly FixedDocument _document;
    private readonly string _title;
    private readonly int _pageCount;

    private PrintTicket _ticket = new();

    public PrintPreviewWindow(FixedDocument document, string title)
    {
        _document = document;
        _title = title;
        _pageCount = document.Pages.Count;

        InitializeComponent();

        Viewer.Document = document;
        PageCountText.Text = $"{_pageCount.ToString("N0", CultureInfo.InvariantCulture)} صفحة";

        FromBox.Text = "1";
        ToBox.Text = _pageCount.ToString(CultureInfo.InvariantCulture);

        LoadPrinters();
    }

    // ------------------------------------------------------------- printers

    private void LoadPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();

            PrintQueue[] queues = server.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            }).ToArray();

            PrinterBox.ItemsSource = queues;
            PrinterBox.DisplayMemberPath = nameof(PrintQueue.FullName);

            // Pre-select whatever Windows considers the default.
            PrintQueue? preferred = LocalPrintServer.GetDefaultPrintQueue();
            PrinterBox.SelectedItem =
                queues.FirstOrDefault(q => preferred is not null && q.FullName == preferred.FullName)
                ?? queues.FirstOrDefault();
        }
        catch (Exception)
        {
            // A machine with no printers installed still gets a working preview.
            PrinterBox.IsEnabled = false;
        }
    }

    private PrintQueue? SelectedQueue => PrinterBox.SelectedItem as PrintQueue;

    /// <summary>A4 portrait unless the driver's own dialog was used to say otherwise.</summary>
    private PrintTicket BuildTicket()
    {
        var ticket = _ticket.Clone();

        try
        {
            ticket.PageMediaSize ??= new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.PageOrientation ??= PageOrientation.Portrait;
            ticket.CopyCount = ReadNumber(CopiesBox.Text, 1, 1, 999);
        }
        catch (Exception)
        {
            // Not every driver exposes every setting; printing still proceeds.
        }

        return ticket;
    }

    // --------------------------------------------------------------- actions

    private void OnAdvancedProperties(object sender, RoutedEventArgs e)
    {
        // The driver's own dialog is the only honest place for settings we do not model:
        // duplex, trays, quality, colour. Whatever it returns becomes our ticket.
        var dialog = new PrintDialog();

        if (SelectedQueue is not null)
        {
            dialog.PrintQueue = SelectedQueue;
        }

        dialog.PrintTicket = BuildTicket();

        if (dialog.ShowDialog() != true) return;

        _ticket = dialog.PrintTicket;

        if (dialog.PrintQueue is not null)
        {
            PrintQueue chosen = dialog.PrintQueue;
            PrinterBox.SelectedItem = PrinterBox.Items
                .OfType<PrintQueue>()
                .FirstOrDefault(q => q.FullName == chosen.FullName) ?? PrinterBox.SelectedItem;
        }

        CopiesBox.Text = (dialog.PrintTicket.CopyCount ?? 1).ToString(CultureInfo.InvariantCulture);
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PrintDialog();

            if (SelectedQueue is not null)
            {
                dialog.PrintQueue = SelectedQueue;
            }

            dialog.PrintTicket = BuildTicket();

            int from = ReadNumber(FromBox.Text, 1, 1, _pageCount);
            int to = ReadNumber(ToBox.Text, _pageCount, 1, _pageCount);

            DocumentPaginator paginator = _document.DocumentPaginator;

            if (from > 1 || to < _pageCount)
            {
                paginator = new PageRangePaginator(paginator, from, to);
            }

            if (paginator.PageCount == 0)
            {
                MessageBox.Show(this, "نطاق الصفحات المحدد فارغ.", "الطباعة",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dialog.PrintDocument(paginator, _title);
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

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private static int ReadNumber(string text, int fallback, int minimum, int maximum) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
}
