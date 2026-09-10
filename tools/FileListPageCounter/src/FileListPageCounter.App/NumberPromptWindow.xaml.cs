using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace FileListPageCounter.App;

/// <summary>A small "type a number" prompt, used for the custom rows-per-page choice.</summary>
public partial class NumberPromptWindow : Window
{
    private readonly int _minimum;
    private readonly int _maximum;

    public NumberPromptWindow(string title, string prompt, int current, int minimum, int maximum)
    {
        _minimum = minimum;
        _maximum = maximum;

        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        RangeText.Text = $"من {minimum} إلى {maximum}";
        ValueBox.Text = current.ToString(CultureInfo.InvariantCulture);
        ValueBox.SelectAll();
        ValueBox.Focus();
    }

    public int Value { get; private set; }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ValueBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            RangeText.Text = $"اكتب رقمًا صحيحًا من {_minimum} إلى {_maximum}";
            return;
        }

        Value = Math.Clamp(parsed, _minimum, _maximum);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
