using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuickBoard.Core;

namespace QuickBoard.Views;

public partial class SettingsDialog : Window
{
    private readonly BoardConfig _config;
    private readonly ConfigStore _store;
    private readonly Action<BoardOptions>? _preview;
    private readonly string? _hotkeyError;

    /// <summary>初始化期间不要被 ValueChanged 回写，否则先赋值的滑块会把另一个的值清零。</summary>
    private bool _loading = true;

    /// <summary>热键的暂存值：热键框里改的是它，点「保存」才写回配置。</summary>
    private HotkeySetting _pendingHotkey = new();

    /// <summary>导入但尚未保存的完整配置；点「保存」才应用，取消则整份丢弃。</summary>
    private BoardConfig? _pendingImport;

    public SettingsDialog(BoardConfig config, ConfigStore store, Action<BoardOptions>? preview = null,
        string? hotkeyError = null)
    {
        InitializeComponent();

        _config = config;
        _store = store;
        _preview = preview;
        _hotkeyError = hotkeyError;

        // 克隆一份改，取消时原设置不受影响
        Options = config.Options.Clone();
        _pendingHotkey = CloneHotkey(config.Hotkey);

        // 控件初值与「导入后同步」共用一套逻辑
        SyncOptionsToControls();
        UpdateHotkeyText();
        RefreshDiagnostics();
    }

    private static HotkeySetting CloneHotkey(HotkeySetting s) => new()
    {
        Ctrl = s.Ctrl,
        Alt = s.Alt,
        Shift = s.Shift,
        Win = s.Win,
        Key = s.Key
    };

    public BoardOptions Options { get; private set; }

    public bool GroupsChanged { get; private set; }

    /// <summary>给离屏渲染自检用，确认滑块初值确实生效。</summary>
    public string DebugState =>
        $"ui={OpacitySlider.Value:0} img={ImageOpacitySlider.Value:0} " +
        $"label={OpacityLabel.Text}/{ImageOpacityLabel.Text} option={Options.BackgroundImageOpacity:0.00} " +
        $"fit={Options.BackgroundFit} crop={Options.BackgroundCropX:0.00},{Options.BackgroundCropY:0.00}," +
        $"{Options.BackgroundCropWidth:0.00},{Options.BackgroundCropHeight:0.00} preview={(BgPreviewImage?.Background is null ? "none" : "ok")} " +
        $"theme={Options.ThemeKind} scale={Options.IconScale} nameFont={Options.NameFontSize:0} diag={(DiagnosticsBox is null ? "none" : DiagnosticsBox.Text.Length + "ch")}";

    private void UpdateLabels()
    {
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
        ImageOpacityLabel.Text = $"{ImageOpacitySlider.Value:0}%";
        if (NameFontLabel is not null)
            NameFontLabel.Text = $"{NameFontSizes.Clamp(NameFontSlider.Value):0}";
    }

    private void NameFont_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || NameFontSlider is null) return;
        UpdateLabels();
        Options.NameFontSize = NameFontSizes.Clamp(NameFontSlider.Value);
        _preview?.Invoke(Options);
    }

    private void UpdateHotkeyText() => HotkeyText.Text = _pendingHotkey.ToString();

    private void Appearance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel is null || ImageOpacityLabel is null) return;

        UpdateLabels();
        if (_loading) return;

        Options.UiOpacity = OpacitySlider.Value / 100.0;
        Options.BackgroundImageOpacity = ImageOpacitySlider.Value / 100.0;
        Options.BackgroundImage = ImageBox.Text.Trim();
        _preview?.Invoke(Options);
    }

    private void ThemeScale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo is null || IconScaleCombo is null) return;
        Options.ThemeKind = ThemeCombo.SelectedIndex;
        Options.IconScale = IconScaleCombo.SelectedIndex;
        _preview?.Invoke(Options);
    }

    private void RefreshDiagnostics()
    {
        if (DiagnosticsBox is null) return;
        DiagnosticsBox.Text = Diagnostics.Collect(_store, _hotkeyError);
    }

    private void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => RefreshDiagnostics();

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        ImageBox.Text = dialog.FileName;
        Options.BackgroundImage = dialog.FileName;
        _preview?.Invoke(Options);
        RefreshBgPreview();
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        ImageBox.Text = "";
        Options.BackgroundImage = "";
        _preview?.Invoke(Options);
        RefreshBgPreview();
    }

    // ---------- 背景图预览与裁剪 ----------

    private void UpdateCropLabels()
    {
        CropXLabel.Text = $"{CropXSlider.Value:0.00}";
        CropYLabel.Text = $"{CropYSlider.Value:0.00}";
        CropWLabel.Text = $"{CropWSlider.Value:0.00}";
        CropHLabel.Text = $"{CropHSlider.Value:0.00}";
    }

    /// <summary>把当前滑块值同步到 Options，并刷新主窗口实时预览与本对话框的预览框。</summary>
    private void SyncCrop()
    {
        Options.BackgroundFit = FitCombo.SelectedIndex;
        // 保证裁剪框不越出图像右/下边界
        double x = CropXSlider.Value;
        double y = CropYSlider.Value;
        Options.BackgroundCropX = x;
        Options.BackgroundCropY = y;
        Options.BackgroundCropWidth = Math.Min(CropWSlider.Value, 1 - x);
        Options.BackgroundCropHeight = Math.Min(CropHSlider.Value, 1 - y);
        _preview?.Invoke(Options);
    }

    private void Crop_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateCropLabels();
        SyncCrop();
        RefreshBgPreview();
    }

    private void CropSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || CropXSlider is null || CropYSlider is null
            || CropWSlider is null || CropHSlider is null) return;
        UpdateCropLabels();
        SyncCrop();
        RefreshBgPreview();
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        FitCombo.SelectedIndex = 0;
        CropXSlider.Value = 0;
        CropYSlider.Value = 0;
        CropWSlider.Value = 1;
        CropHSlider.Value = 1;
        _loading = false;
        UpdateCropLabels();
        SyncCrop();
        RefreshBgPreview();
    }

    private void RefreshBgPreview()
    {
        if (BgPreviewImage is null) return;
        var brush = Appearance.LoadImageBrush(Options.BackgroundImage, Options.BackgroundFit,
            Options.BackgroundCropX, Options.BackgroundCropY,
            Options.BackgroundCropWidth, Options.BackgroundCropHeight);
        BgPreviewImage.Background = brush;
        BgPreviewHint.Visibility = brush is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyDialog(_pendingHotkey) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        // 只改暂存值：点「保存」才写入配置，取消则原热键不变
        _pendingHotkey = dialog.Result;
        UpdateHotkeyText();
    }

    private void ImportMal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 MadAppLauncher 配置文件",
            Filter = "MadAppLauncher 配置|*.mal|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = MalImporter.ImportMal(dialog.FileName);
            // 与 JSON 导入一致：只暂存分组，点「保存」才写回配置（取消零副作用）
            _pendingImport = new BoardConfig
            {
                Version = _config.Version,
                Groups = imported.Groups,
                HomeGroupId = "",
                LastGroupIndex = 0
            };
            GroupsChanged = true;
            MessageBox.Show(this,
                $"已载入 {imported.Groups.Count} 个分组，共 {imported.Groups.Sum(g => g.Entries.Count)} 个条目。点「保存」后生效。",
                "导入", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BackupConfig_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(_store.BaseDirectory,
            $"board.backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var ok = _store.Export(_config, path, out var message);
        MessageBox.Show(this, message, ok ? "备份成功" : "备份失败",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出配置",
            Filter = "QuickBoard 配置|*.json|所有文件|*.*",
            FileName = $"QuickBoard-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != true) return;

        var ok = _store.Export(_config, dialog.FileName, out var message);
        MessageBox.Show(this, message, ok ? "导出成功" : "导出失败",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入配置",
            Filter = "QuickBoard 配置|*.json|所有文件|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != true) return;

        var imported = _store.ReadImport(dialog.FileName, out var message);
        if (imported is null)
        {
            MessageBox.Show(this, message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            message + "\n\n导入会替换当前全部分组与设置（原配置会自动留存 .bak 备份）。是否继续？",
            "导入配置", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        // 只暂存：点「保存」才写入配置并落盘，点「取消」整份丢弃（零副作用）
        _pendingImport = imported;
        _pendingHotkey = CloneHotkey(imported.Hotkey);
        Options = imported.Options.Clone();
        SyncOptionsToControls();
        GroupsChanged = true;

        UpdateHotkeyText();
        RefreshDiagnostics();
        _preview?.Invoke(Options);

        MessageBox.Show(this, "配置已载入。点「保存」后生效，点「取消」则不改变现有配置。", "导入预览",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>把 Options 的当前值写回所有控件（导入配置后用）。</summary>
    private void SyncOptionsToControls()
    {
        _loading = true;
        HideAfterLaunchCheck.IsChecked = Options.HideAfterLaunch;
        HideOnDeactivateCheck.IsChecked = Options.HideOnDeactivate;
        AlwaysOnTopCheck.IsChecked = Options.AlwaysOnTop;
        CenterCheck.IsChecked = Options.CenterOnScreen;
        FavoritesBarCheck.IsChecked = Options.ShowFavoritesBar;
        TrayCheck.IsChecked = Options.ShowTrayIcon;
        StartHiddenCheck.IsChecked = Options.StartHidden;
        StartupCheck.IsChecked = Options.StartWithWindows || StartupRegistrar.IsEnabled();
        OpacitySlider.Value = Math.Round(Options.UiOpacity * 100);
        ImageOpacitySlider.Value = Math.Round(Options.BackgroundImageOpacity * 100);
        ImageBox.Text = Options.BackgroundImage;
        FitCombo.SelectedIndex = Math.Clamp(Options.BackgroundFit, 0, 2);
        CropXSlider.Value = Options.BackgroundCropX;
        CropYSlider.Value = Options.BackgroundCropY;
        CropWSlider.Value = Options.BackgroundCropWidth;
        CropHSlider.Value = Options.BackgroundCropHeight;
        ThemeCombo.SelectedIndex = Math.Clamp(Options.ThemeKind, 0, 2);
        IconScaleCombo.SelectedIndex = Math.Clamp(Options.IconScale, 0, 2);
        NameFontSlider.Value = NameFontSizes.Clamp(Options.NameFontSize);
        _loading = false;
        UpdateLabels();
        UpdateCropLabels();
        RefreshBgPreview();
    }

    private void OpenConfigDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_store.BaseDirectory) { UseShellExecute = true });
        }
        catch
        {
            // 打不开目录不影响其它设置
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Options.HideAfterLaunch = HideAfterLaunchCheck.IsChecked == true;
        Options.HideOnDeactivate = HideOnDeactivateCheck.IsChecked == true;
        Options.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        Options.CenterOnScreen = CenterCheck.IsChecked == true;
        Options.ShowFavoritesBar = FavoritesBarCheck.IsChecked == true;
        Options.ShowTrayIcon = TrayCheck.IsChecked == true;
        Options.StartHidden = StartHiddenCheck.IsChecked == true;
        Options.StartWithWindows = StartupCheck.IsChecked == true;

        Options.UiOpacity = OpacitySlider.Value / 100.0;
        Options.BackgroundImageOpacity = ImageOpacitySlider.Value / 100.0;
        Options.BackgroundImage = ImageBox.Text.Trim();
        Options.ThemeKind = ThemeCombo.SelectedIndex;
        Options.IconScale = IconScaleCombo.SelectedIndex;
        Options.NameFontSize = NameFontSizes.Clamp(NameFontSlider.Value);
        // 裁剪值在 SyncCrop 里已写入 Options，这里兜底同步滑块终值
        SyncCrop();

        if (Options.StartWithWindows != StartupRegistrar.IsEnabled())
            StartupRegistrar.Set(Options.StartWithWindows);

        // 暂存的导入配置在「保存」这一刻才真正写入（取消则整份丢弃）；
        // 落盘交给主窗口的 Save —— 它会先把导入前的配置轮转为 .bak，备份链更干净
        if (_pendingImport is { } import)
        {
            _config.Version = import.Version;
            _config.Groups = import.Groups;
            _config.HomeGroupId = import.HomeGroupId;
            _config.LastGroupIndex = import.LastGroupIndex;
        }

        _config.Hotkey = _pendingHotkey;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
