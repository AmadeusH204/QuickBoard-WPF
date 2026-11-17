using System.Windows;
using System.Windows.Input;
using QuickBoard.Core;

namespace QuickBoard.Views;

public partial class HotkeyDialog : Window
{
    private HotkeySetting _candidate;
    private bool _captured;

    public HotkeyDialog(HotkeySetting current)
    {
        InitializeComponent();
        _candidate = new HotkeySetting
        {
            Ctrl = current.Ctrl,
            Alt = current.Alt,
            Shift = current.Shift,
            Win = current.Win,
            Key = current.Key
        };
        _captured = true;
        KeyText.Text = _candidate.ToString();
        Loaded += (_, _) => Focus();
    }

    public HotkeySetting? Result { get; private set; }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            KeyText.Text = "请包含 Ctrl / Alt / Win 之一";
            _captured = false;
            return;
        }

        _candidate = new HotkeySetting
        {
            Ctrl = (modifiers & ModifierKeys.Control) != 0,
            Alt = (modifiers & ModifierKeys.Alt) != 0,
            Shift = (modifiers & ModifierKeys.Shift) != 0,
            Win = (modifiers & ModifierKeys.Windows) != 0,
            Key = key.ToString()
        };

        KeyText.Text = _candidate.ToString();
        _captured = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_captured)
        {
            MessageBox.Show(this, "请先按下一个带 Ctrl / Alt / Win 的组合键。", "热键",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = _candidate;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
