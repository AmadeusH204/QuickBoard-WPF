using System.Windows;
using QuickBoard.Core;

namespace QuickBoard.Views;

public partial class EditEntryDialog : Window
{
    private readonly AppEntry _entry;

    public EditEntryDialog(AppEntry entry)
    {
        InitializeComponent();
        _entry = entry;

        NameBox.Text = entry.Name;
        TargetBox.Text = entry.Target;
        ArgumentsBox.Text = entry.Arguments;
        WorkingDirBox.Text = entry.WorkingDirectory;
        SlotBox.Text = SlotKeys.Label(entry.Slot);
        FavBox.Text = entry.FunctionKey > 0 ? "F" + entry.FunctionKey : "";
        PlacementBox.Text = entry.WindowPlacement;
        AdminCheck.IsChecked = entry.RunAsAdmin;

        Loaded += (_, _) => NameBox.Focus();
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择目标",
            Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) == true) TargetBox.Text = dialog.FileName;
    }

    private void BrowseWorkingDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择起始目录" };
        if (dialog.ShowDialog(this) == true) WorkingDirBox.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var slotText = SlotBox.Text.Trim();
        var slot = SlotKeys.FromLabel(slotText);
        if (slotText.Length > 0 && slot == 0)
        {
            MessageBox.Show(this, "键位只支持 Q W E R T Y U I O P A S D F G H J K L ; Z X C V B N M , . /",
                "键位", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _entry.Name = NameBox.Text.Trim();
        _entry.Target = TargetBox.Text.Trim();
        _entry.Arguments = ArgumentsBox.Text.Trim();
        _entry.WorkingDirectory = WorkingDirBox.Text.Trim();
        _entry.Slot = slot;
        _entry.FunctionKey = ParseFunctionKey(FavBox.Text.Trim());
        _entry.WindowPlacement = PlacementBox.Text.Trim();
        _entry.RunAsAdmin = AdminCheck.IsChecked == true;

        DialogResult = true;
    }

    /// <summary>「F3」「3」都接受；空或 0 表示不绑收藏栏。</summary>
    private static int ParseFunctionKey(string text)
    {
        if (text.Length == 0) return 0;
        var digits = text.TrimStart('F', 'f');
        return int.TryParse(digits, out var n) && n >= 1 && n <= 12 ? n : 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
