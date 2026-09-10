using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FileListPageCounter.Core.Common;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;

namespace FileListPageCounter.App.Printing;

/// <summary>
/// Draws the report as real A4 pages.
///
/// One renderer serves both the live preview and the printer, so what the user sees on screen is
/// literally the thing that comes out of the tray — there is no second layout that could drift
/// from the first. It also follows the same pagination rules as the Word writer, so all three
/// break the pages in the same places.
/// </summary>
public static class ReportPageRenderer
{
    // A4 at 96 dpi: 210 × 297 mm.
    public const double PageWidth = 793.7;
    public const double PageHeight = 1122.5;

    private const double SideMargin = 75.6;   // 2 cm
    private const double TopMargin = 83.1;    // 2.2 cm
    private const double BottomMargin = 75.6;

    private const double UsableWidth = PageWidth - (2 * SideMargin);

    /// <summary>Points to device-independent pixels.</summary>
    private static double Px(double points) => points * 96d / 72d;

    private static readonly Brush Accent = Freeze("#1F4E79");
    private static readonly Brush AccentSoft = Freeze("#2E75B6");
    private static readonly Brush OnAccent = Brushes.White;
    private static readonly Brush BandFill = Freeze("#F2F6FA");
    private static readonly Brush PanelFill = Freeze("#F7F9FC");
    private static readonly Brush BorderBrush = Freeze("#BFCBD9");
    private static readonly Brush TextBrush = Freeze("#1F2430");
    private static readonly Brush MutedBrush = Freeze("#5A6B7C");

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Draws the pages. <paramref name="maxPages"/> caps the work for the on-screen preview,
    /// where drawing ten thousand rows would stall the window for no benefit; printing passes
    /// zero and gets the whole document.
    /// </summary>
    public static FixedDocument Render(IReadOnlyList<ReportRow> rows, ReportOptions options, int maxPages = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);

        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(PageWidth, PageHeight);

        int blocks = ReportLayout.NormalizeBlocks(options.ColumnBlocks);
        IReadOnlyList<ReportRow?[]> arranged = ReportLayout.Arrange(rows, blocks);

        // An unpinned row count still has to become a concrete number to draw pages at all;
        // the estimate the Word writer uses is exactly the right one to borrow.
        int rowsPerPage = options.RowsPerPage > 0
            ? options.RowsPerPage
            : ReportLayout.RowsThatFitOnAPage(options.FontSize, firstPage: false);

        int firstPageRows = ReportLayout.FirstPageRows(options.FontSize, rowsPerPage);

        IReadOnlyList<IReadOnlyList<ReportRow?[]>> pages =
            ReportLayout.Paginate(arranged, rowsPerPage, firstPageRows);

        ReportTotals totals = ReportTotals.From(rows);

        int render = maxPages > 0 ? Math.Min(maxPages, pages.Count) : pages.Count;

        for (int i = 0; i < render; i++)
        {
            FixedPage page = BuildPage(pages[i], i == 0, i + 1, pages.Count, totals, options, blocks);

            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        return document;
    }

    // ------------------------------------------------------------------ page

    private static FixedPage BuildPage(
        IReadOnlyList<ReportRow?[]> lines,
        bool isFirstPage,
        int pageNumber,
        int pageCount,
        ReportTotals totals,
        ReportOptions options,
        int blocks)
    {
        var page = new FixedPage
        {
            Width = PageWidth,
            Height = PageHeight,
            Background = Brushes.White,
            FlowDirection = FlowDirection.RightToLeft
        };

        var body = new StackPanel { Width = UsableWidth, FlowDirection = FlowDirection.RightToLeft };

        // Running header: the report title, quiet, with a rule under it.
        body.Children.Add(new Border
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4),
            Margin = new Thickness(0, 0, 0, 10),
            Child = Text(options.Title, ReportTheme.Step(options.FontSize, ReportTheme.MetaStep), MutedBrush, TextAlignment.Right)
        });

        if (isFirstPage)
        {
            body.Children.Add(TitleBlock(options));
            body.Children.Add(FigureBand(totals, options));
        }

        body.Children.Add(DataTable(lines, options, blocks));

        if (pageNumber == pageCount)
        {
            body.Children.Add(SummaryBlock(totals, options));
        }

        FixedPage.SetTop(body, TopMargin);
        FixedPage.SetRight(body, SideMargin);
        page.Children.Add(body);

        page.Children.Add(Footer(pageNumber, options));
        return page;
    }

    private static UIElement TitleBlock(ReportOptions options)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14), FlowDirection = FlowDirection.RightToLeft };

        panel.Children.Add(new Border
        {
            BorderBrush = AccentSoft,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 6),
            Child = Text(
                options.Title,
                ReportTheme.Step(options.FontSize, ReportTheme.TitleStep),
                Accent,
                TextAlignment.Center,
                bold: true)
        });

        string stamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture);
        TextBlock meta = Text(
            $"تاريخ الإنشاء: {stamp}",
            ReportTheme.Step(options.FontSize, ReportTheme.MetaStep),
            MutedBrush,
            TextAlignment.Center);
        meta.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(meta);

        return panel;
    }

    private static UIElement FigureBand(ReportTotals totals, ReportOptions options)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 16), FlowDirection = FlowDirection.RightToLeft };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddFigure(grid, 0, Strings.TotalFiles, totals.Files, options);
        AddFigure(grid, 1, Strings.TotalPages, totals.Pages, options);
        AddFigure(grid, 2, Strings.UnknownFiles, totals.Unknown, options);

        return new Border
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Background = PanelFill,
            FlowDirection = FlowDirection.RightToLeft,
            Child = grid
        };
    }

    private static void AddFigure(Grid grid, int column, string label, long value, ReportOptions options)
    {
        var panel = new StackPanel { Margin = new Thickness(6, 8, 6, 8), FlowDirection = FlowDirection.RightToLeft };

        panel.Children.Add(Text(
            value.ToString("N0", CultureInfo.InvariantCulture),
            ReportTheme.Step(options.FontSize, ReportTheme.FigureStep),
            Accent,
            TextAlignment.Center,
            bold: true));

        TextBlock caption = Text(
            label,
            ReportTheme.Step(options.FontSize, ReportTheme.CaptionStep),
            MutedBrush,
            TextAlignment.Center);
        caption.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(caption);

        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    // ----------------------------------------------------------------- table

    private static UIElement DataTable(IReadOnlyList<ReportRow?[]> lines, ReportOptions options, int blocks)
    {
        (int indexTwips, int nameTwips, int countTwips) =
            ReportLayout.BlockColumnWidths(9638, blocks);

        double scale = UsableWidth / 9638d;

        // Right to left, so the first column of the first block sits on the right edge.
        var grid = new Grid { FlowDirection = FlowDirection.RightToLeft };

        for (int block = 0; block < blocks; block++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indexTwips * scale) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameTwips * scale) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(countTwips * scale) });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The header repeats because every page draws its own copy.
        for (int block = 0; block < blocks; block++)
        {
            int c = block * 3;
            grid.Children.Add(Cell(Strings.ColumnIndex, 0, c, options, OnAccent, Accent, TextAlignment.Center, bold: true));
            grid.Children.Add(Cell(Strings.ColumnFileName, 0, c + 1, options, OnAccent, Accent, TextAlignment.Center, bold: true));
            grid.Children.Add(Cell(Strings.ColumnPages, 0, c + 2, options, OnAccent, Accent, TextAlignment.Center, bold: true));
        }

        bool banded = false;
        int rowIndex = 1;

        foreach (ReportRow?[] cells in lines)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Brush? fill = banded ? BandFill : null;

            for (int block = 0; block < blocks; block++)
            {
                int c = block * 3;
                ReportRow? row = cells[block];

                if (row is null)
                {
                    grid.Children.Add(Cell(string.Empty, rowIndex, c, options, TextBrush, fill, TextAlignment.Center));
                    grid.Children.Add(Cell(string.Empty, rowIndex, c + 1, options, TextBrush, fill, TextAlignment.Right));
                    grid.Children.Add(Cell(string.Empty, rowIndex, c + 2, options, TextBrush, fill, TextAlignment.Center));
                    continue;
                }

                grid.Children.Add(Cell(
                    row.Index.ToString(CultureInfo.InvariantCulture),
                    rowIndex, c, options, MutedBrush, fill, TextAlignment.Center));

                grid.Children.Add(Cell(row.Name, rowIndex, c + 1, options, TextBrush, fill, TextAlignment.Right));

                grid.Children.Add(Cell(
                    row.PageCountText,
                    rowIndex, c + 2, options,
                    row.PageCount.HasValue ? TextBrush : MutedBrush,
                    fill, TextAlignment.Center));
            }

            banded = !banded;
            rowIndex++;
        }

        return new Border
        {
            BorderBrush = Accent,
            BorderThickness = new Thickness(0, 1.5, 0, 1.5),
            FlowDirection = FlowDirection.RightToLeft,
            Child = grid
        };
    }

    private static UIElement Cell(
        string text,
        int row,
        int column,
        ReportOptions options,
        Brush foreground,
        Brush? background,
        TextAlignment alignment,
        bool bold = false)
    {
        TextBlock block = Text(text, options.FontSize, foreground, alignment, bold);
        block.Margin = new Thickness(5, 3, 5, 3);
        block.TextTrimming = TextTrimming.CharacterEllipsis;

        var border = new Border
        {
            Background = background,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.5),
            FlowDirection = FlowDirection.RightToLeft,
            Child = block
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        return border;
    }

    // --------------------------------------------------------------- summary

    private static UIElement SummaryBlock(ReportTotals totals, ReportOptions options)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0), FlowDirection = FlowDirection.RightToLeft };

        panel.Children.Add(new Border
        {
            BorderBrush = AccentSoft,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4),
            Margin = new Thickness(0, 0, 0, 6),
            Child = Text(Strings.Summary, options.FontSize, Accent, TextAlignment.Right, bold: true)
        });

        panel.Children.Add(Text($"{Strings.TotalFiles}: {totals.Files:N0}", options.FontSize, TextBrush, TextAlignment.Right));
        panel.Children.Add(Text($"{Strings.TotalPages}: {totals.Pages:N0}", options.FontSize, TextBrush, TextAlignment.Right));
        panel.Children.Add(Text($"{Strings.UnknownFiles}: {totals.Unknown:N0}", options.FontSize, TextBrush, TextAlignment.Right));

        return panel;
    }

    // ---------------------------------------------------------------- footer

    private static UIElement Footer(int pageNumber, ReportOptions options)
    {
        var panel = new StackPanel { Width = UsableWidth, FlowDirection = FlowDirection.RightToLeft };

        panel.Children.Add(Text(
            $"{Strings.PageOf} {pageNumber}",
            ReportTheme.Step(options.FontSize, ReportTheme.MetaStep),
            MutedBrush,
            TextAlignment.Center));

        if (options.HasUserSignature)
        {
            TextBlock signature = Text(
                $"{Strings.CompiledBy}: {options.UserName}",
                ReportOptions.SignatureFontSize,
                MutedBrush,
                TextAlignment.Center);
            signature.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(signature);
        }

        FixedPage.SetRight(panel, SideMargin);
        FixedPage.SetBottom(panel, BottomMargin / 2);
        return panel;
    }

    private static TextBlock Text(
        string text,
        double pointSize,
        Brush foreground,
        TextAlignment alignment,
        bool bold = false) =>
        new()
        {
            Text = text,
            FontFamily = new FontFamily("Arial, Tahoma, Segoe UI"),
            FontSize = Px(pointSize),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = foreground,
            TextAlignment = alignment,
            FlowDirection = FlowDirection.RightToLeft
        };
}
