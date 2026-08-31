using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FileListPageCounter.App.Infrastructure;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;

namespace FileListPageCounter.App;

/// <summary>
/// The step between pressing an export button and choosing where to save: it states what is
/// about to be produced, and lets the user shape it — title, layout, font size — while showing
/// the resulting page count update as they choose.
/// </summary>
public partial class ExportOptionsWindow : Window
{
    private readonly ExportRequest _request;
    private bool _ready;

    public ExportOptionsWindow(ExportRequest request)
    {
        _request = request;
        InitializeComponent();

        HeadingText.Text = $"تصدير إلى {request.FormatName}";
        SubHeadingText.Text =
            $"{Num(request.EntryCount)} ملفًا • إجمالي {Num(request.TotalPages)} صفحة داخل الملفات";

        TitleBox.Text = request.Title;

        FontSizeBox.ItemsSource = ReportOptions.AllowedFontSizes;
        FontSizeBox.SelectedItem = request.FontSize;

        // Only a paginated document has pages to spread across; a spreadsheet is one long sheet.
        LayoutPanel.Visibility = request.Paginated ? Visibility.Visible : Visibility.Collapsed;

        TwoBlocks.IsChecked = request.ColumnBlocks == 2;
        ThreeBlocks.IsChecked = request.ColumnBlocks == 3;
        OneBlock.IsChecked = request.ColumnBlocks <= 1;

        _ready = true;
        UpdateEstimate();
    }

    public ExportChoice? Choice { get; private set; }

    private int SelectedBlocks =>
        ThreeBlocks.IsChecked == true ? 3 :
        TwoBlocks.IsChecked == true ? 2 : 1;

    private int SelectedFontSize =>
        FontSizeBox.SelectedItem is int size ? size : ReportOptions.DefaultFontSize;

    private void OnLayoutChanged(object sender, RoutedEventArgs e) => UpdateEstimate();

    // WPF binds XAML handlers by exact delegate signature, so SelectionChanged needs its own.
    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e) => UpdateEstimate();

    private void UpdateEstimate()
    {
        if (!_ready) return;

        int blocks = SelectedBlocks;
        int fontSize = SelectedFontSize;

        if (_request.Paginated)
        {
            int pages = ReportLayout.EstimatePages(_request.EntryCount, fontSize, blocks);
            int single = ReportLayout.EstimatePages(_request.EntryCount, fontSize, 1);

            EstimateText.Text = $"سيتم إخراج التقرير في {Num(pages)} صفحة تقريبًا";

            EstimateDetail.Text = blocks == 1
                ? "عدد الصفحات تقديري؛ Word يحدّد التقسيم النهائي عند الفتح."
                : $"بدل {Num(single)} صفحة عند استخدام عمود واحد — توفير {Num(Math.Max(0, single - pages))} صفحة.";
        }
        else
        {
            int rows = _request.EntryCount + 10; // the title block, figures, header and total row
            EstimateText.Text = $"سيتم إخراج جدول من {Num(_request.EntryCount)} صفًا";
            EstimateDetail.Text = $"ورقة واحدة بطول {Num(rows)} صفًا، مع فلتر وتجميد للرأس.";
        }

        // A narrow block cannot hold a long file name at a large size.
        bool tight = blocks >= 2 && fontSize >= 20;
        FontHint.Text = tight
            ? "مع عمودين أو أكثر يُنصح بحجم خط 16 أو 18 حتى تظهر أسماء الملفات الطويلة كاملة."
            : string.Empty;
        FontHint.Visibility = tight ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        Choice = new ExportChoice
        {
            Title = TitleBox.Text,
            FontSize = SelectedFontSize,
            ColumnBlocks = SelectedBlocks,
            OpenWhenDone = OpenWhenDoneBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
