using System.Windows;
using System.Windows.Input;

namespace QuickBoard.Views;

public partial class PromptDialog : Window
{
    public PromptDialog(string title, string label, string value)
    {
        InitializeComponent();

        TitleText.Text = title;
        LabelText.Text = label;
        ValueBox.Text = value;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };

        ValueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Ok_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            ValueBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
