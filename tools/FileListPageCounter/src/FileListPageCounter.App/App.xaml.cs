using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FileListPageCounter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Arabic UI: dates, numbers and the default FlowDirection of every element follow ar-SA.
        var culture = new CultureInfo("ar-SA");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += OnUnhandledException;

        base.OnStartup(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "حدث خطأ غير متوقع:\n\n" + e.Exception.Message,
            "خطأ",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
