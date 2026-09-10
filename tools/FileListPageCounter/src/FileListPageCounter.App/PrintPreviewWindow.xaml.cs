using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;   // PrintDialog lives here, not in System.Windows
using System.Windows.Documents;
using System.Windows.Input;
using FileListPageCounter.App.Printing;
using FileListPageCounter.Core.Models;

namespace FileListPageCounter.App;

/// <summary>
/// Shows the very pages that will be printed, and prints them.
///
/// The window draws the document itself from the rows and a copy of the report options, which is
/// what lets the switches along the top take effect immediately: tick one off and the pages below
/// are redrawn without it, so what is reviewed is always what comes out of the tray.
///
/// The options are a copy until the window closes; whatever state they are in then is handed back
/// to the caller, so a choice made here also carries over to the main preview and to Word/Excel.
///
/// Printing goes straight to the chosen printer with the settings on this panel; the driver's
/// own properties dialog is one button away for anything the panel does not cover.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    private readonly IReadOnlyList<ReportRow> _rows;

    private FixedDocument _document = new();
    private int _pageCount;
    private bool _loaded;

    private PrintTicket _ticket = new();

    /// <summary>The options as the user left them — the caller adopts these when the window closes.</summary>
    public ReportOptions Options { get; }

    public PrintPreviewWindow(IReadOnlyList<ReportRow> rows, ReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);

        _rows = rows;
        Options = options.Clone();

        InitializeComponent();

        ShowTitleBox.IsChecked = Options.ShowTitle;
        ShowDateBox.IsChecked = Options.ShowDateLine;
        ShowTotalsBox.IsChecked = Options.ShowTotalsBand;
        ShowRunningHeaderBox.IsChecked = Options.ShowRunningHeader;
        ShowSummaryBox.IsChecked = Options.ShowSummary;
        ShowPageNumbersBox.IsChecked = Options.IncludePageNumbers;
        ShowNameBox.IsChecked = Options.ShowUserName;

        // A name that was never typed cannot be shown, so the switch says so rather than
        // silently doing nothing when it is ticked.
        ShowNameBox.IsEnabled = Options.UserName.Length > 0;

        _loaded = true;

        RenderDocument(resetRange: true);
        LoadPrinters();
    }

    // ------------------------------------------------------------- rendering

    /// <summary>
    /// Redraws every page from the current options. The page range is only reset when the window
    /// opens; afterwards a range the user typed is kept, clamped to the new page count.
    /// </summary>
    private void RenderDocument(bool resetRange)
    {
        _document = ReportPageRenderer.Render(_rows, Options);
        _pageCount = _document.Pages.Count;

        Viewer.Document = _document;
        PageCountText.Text = $"{_pageCount.ToString("N0", CultureInfo.InvariantCulture)} صفحة";

        if (resetRange)
        {
            FromBox.Text = "1";
            ToBox.Text = _pageCount.ToString(CultureInfo.InvariantCulture);
            return;
        }

        FromBox.Text = ReadNumber(FromBox.Text, 1, 1, Math.Max(_pageCount, 1))
            .ToString(CultureInfo.InvariantCulture);
        ToBox.Text = ReadNumber(ToBox.Text, _pageCount, 1, Math.Max(_pageCount, 1))
            .ToString(CultureInfo.InvariantCulture);
    }

    private void OnSectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        Options.ShowTitle = ShowTitleBox.IsChecked == true;
        Options.ShowDateLine = ShowDateBox.IsChecked == true;
        Options.ShowTotalsBand = ShowTotalsBox.IsChecked == true;
        Options.ShowRunningHeader = ShowRunningHeaderBox.IsChecked == true;
        Options.ShowSummary = ShowSummaryBox.IsChecked == true;
        Options.IncludePageNumbers = ShowPageNumbersBox.IsChecked == true;
        Options.ShowUserName = ShowNameBox.IsChecked == true;

        RenderDocument(resetRange: false);
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

            dialog.PrintDocument(paginator, Options.Title);
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
