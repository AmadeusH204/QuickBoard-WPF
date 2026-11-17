using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickBoard.Core;

/// <summary>一个可启动的条目。字段刻意与 MadAppLauncher 的 MAButton 一一对应，便于导入。</summary>
public sealed class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
    public bool RunAsAdmin { get; set; }

    /// <summary>键位槽 1..30，对应 Q-P / A-; / Z-/ 三排；0 表示不绑定键位。</summary>
    public int Slot { get; set; }

    /// <summary>功能键 1..12 对应 F1–F12（原版收藏栏），0 表示不绑定。</summary>
    public int FunctionKey { get; set; }

    /// <summary>Normal / Maximized / Minimized</summary>
    public string WindowState { get; set; } = "Normal";

    /// <summary>空 = 不干预；LeftHalf / RightHalf / Center / Maximize</summary>
    public string WindowPlacement { get; set; } = "";

    public int WindowMoveDelay { get; set; } = 1000;
}

public sealed class AppGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新分组";

    /// <summary>收藏栏：不参与分组切换，永远置顶显示在每一页最上方（对应原版 Favorites Bar）。</summary>
    public bool IsFavorites { get; set; }

    /// <summary>隐藏的分组：保留数据但不显示在分组栏、不参与数字键/滚轮切换，且不能设为主界面。</summary>
    public bool IsHidden { get; set; }

    public List<AppEntry> Entries { get; set; } = new();
}

public sealed class HotkeySetting
{
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public string Key { get; set; } = "Space";

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

public sealed class BoardOptions
{
    // —— 原版「视图」菜单里的开关 ——
    public bool AlwaysOnTop { get; set; } = true;
    public bool CenterOnScreen { get; set; } = true;
    public bool HideOnDeactivate { get; set; } = true;
    public bool HideAfterLaunch { get; set; } = true;
    public bool ShowFavoritesBar { get; set; } = true;

    // —— 常规 ——
    public bool ShowTrayIcon { get; set; } = true;
    public bool StartHidden { get; set; }
    public bool StartWithWindows { get; set; }

    // —— 外观 ——
    /// <summary>整体界面不透明度 0.2–1，1 为完全不透明。</summary>
    public double UiOpacity { get; set; } = 0.96;

    /// <summary>自定义背景图（空 = 不使用）。</summary>
    public string BackgroundImage { get; set; } = "";

    /// <summary>背景图不透明度 0–1。</summary>
    public double BackgroundImageOpacity { get; set; } = 0.8;

    /// <summary>背景图铺放方式：0=铺满(可裁剪)、1=完整适应、2=拉伸填满。</summary>
    public int BackgroundFit { get; set; }

    /// <summary>背景图裁剪区域（相对坐标 0–1）：只显示原图的这个矩形。默认全图。</summary>
    public double BackgroundCropX { get; set; }
    public double BackgroundCropY { get; set; }
    public double BackgroundCropWidth { get; set; } = 1;
    public double BackgroundCropHeight { get; set; } = 1;

    // —— 窗口尺寸位置（对应原版 frmMain_Size / frmMain_Location）——
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 660;
    public bool HasWindowPosition { get; set; }
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }

    /// <summary>配色主题：0=深色、1=浅色、2=跟随系统。</summary>
    public int ThemeKind { get; set; }

    /// <summary>图标尺寸档位：0=紧凑、1=标准、2=大。</summary>
    public int IconScale { get; set; } = 1;

    /// <summary>磁贴名称字号（键盘磁贴 9–13，默认 11；收藏栏自动小一档）。</summary>
    public double NameFontSize { get; set; } = 11;

    /// <summary>浅拷贝即可：所有字段都是值类型或字符串。</summary>
    public BoardOptions Clone() => (BoardOptions)MemberwiseClone();
}

public static class NameFontSizes
{
    public const double Min = 9;
    public const double Default = 11;
    public const double Max = 13;

    /// <summary>把任意值夹到合法区间；NaN/0 回默认档。</summary>
    public static double Clamp(double value) =>
        double.IsNaN(value) || value <= 0 ? Default : Math.Clamp(value, Min, Max);
}

public static class ThemeKinds
{
    public const int Dark = 0;
    public const int Light = 1;
    public const int System = 2;
}

public static class IconScales
{
    public const int Compact = 0;
    public const int Standard = 1;
    public const int Large = 2;
}

public sealed class BoardConfig
{
    public int Version { get; set; } = 2;
    public List<AppGroup> Groups { get; set; } = new();
    public HotkeySetting Hotkey { get; set; } = new();
    public BoardOptions Options { get; set; } = new();
    public int LastGroupIndex { get; set; }

    /// <summary>主界面分组的 Id。启动/重载时优先显示它；空或失效时回退到 LastGroupIndex。</summary>
    public string HomeGroupId { get; set; } = "";
}

/// <summary>分组的可见性与主界面规则，集中在此便于自检与主窗口共用。</summary>
public static class GroupRules
{
    /// <summary>可参与分组栏与数字键/滚轮切换的分组（非收藏栏、未隐藏）。</summary>
    public static List<int> SelectableIndices(BoardConfig config) =>
        Enumerable.Range(0, config.Groups.Count)
            .Where(i => !config.Groups[i].IsFavorites && !config.Groups[i].IsHidden)
            .ToList();

    /// <summary>解析启动分组：主界面分组有效则用它，否则用上次的，再否则第一个可选分组。</summary>
    public static int ResolveInitialIndex(BoardConfig config)
    {
        var order = SelectableIndices(config);
        if (order.Count == 0) return 0;

        if (!string.IsNullOrWhiteSpace(config.HomeGroupId))
        {
            int home = order.FirstOrDefault(i => config.Groups[i].Id == config.HomeGroupId, -1);
            if (home >= 0) return home;
        }

        return order.Contains(config.LastGroupIndex) ? config.LastGroupIndex : order[0];
    }

    /// <summary>主界面分组当前是否有效（存在、非收藏栏、未隐藏）。</summary>
    public static bool IsHomeValid(BoardConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.HomeGroupId)) return false;
        return config.Groups.Any(g => g.Id == config.HomeGroupId && !g.IsFavorites && !g.IsHidden);
    }
}

/// <summary>物理键位表：Q-P(1-10)、A-;(11-20)、Z-/(21-30)，与 MadAppLauncher 的 buttonID 顺序一致。</summary>
public static class SlotKeys
{
    public static readonly string[] Labels =
    {
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
        "A", "S", "D", "F", "G", "H", "J", "K", "L", ";",
        "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"
    };

    public const int Count = 30;
    public const int Columns = 10;
    public const int Rows = 3;

    public static string Label(int slot) =>
        slot >= 1 && slot <= Count ? Labels[slot - 1] : "";

    /// <summary>槽位所在键盘行：0 = 上排 Q..P，1 = 中排 A..;，2 = 下排 Z../。</summary>
    public static int RowOf(int slot) => slot is >= 1 and <= Count ? (slot - 1) / Columns : -1;

    public static int ColumnOf(int slot) => slot is >= 1 and <= Count ? (slot - 1) % Columns : -1;

    /// <summary>行 + 列反算槽位（1..30）。</summary>
    public static int SlotAt(int row, int column) =>
        row is >= 0 and < Rows && column is >= 0 and < Columns ? row * Columns + column + 1 : 0;

    /// <summary>把用户输入的键名字符转成槽位，无法识别返回 0。</summary>
    public static int FromLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return 0;
        var text = label.Trim();
        for (int i = 0; i < Labels.Length; i++)
        {
            if (string.Equals(Labels[i], text, StringComparison.OrdinalIgnoreCase)) return i + 1;
        }

        return text.ToUpperInvariant() switch
        {
            "SEMICOLON" or "SEMI" => 20,
            "COMMA" => 28,
            "PERIOD" or "DOT" => 29,
            "SLASH" => 30,
            _ => 0
        };
    }

    /// <summary>键盘事件 -> 槽位（1..30），不匹配返回 0。</summary>
    public static int FromKey(Key key)
    {
        int index = key switch
        {
            Key.Q => 0, Key.W => 1, Key.E => 2, Key.R => 3, Key.T => 4,
            Key.Y => 5, Key.U => 6, Key.I => 7, Key.O => 8, Key.P => 9,
            Key.A => 10, Key.S => 11, Key.D => 12, Key.F => 13, Key.G => 14,
            Key.H => 15, Key.J => 16, Key.K => 17, Key.L => 18, Key.OemSemicolon => 19,
            Key.Z => 20, Key.X => 21, Key.C => 22, Key.V => 23, Key.B => 24,
            Key.N => 25, Key.M => 26, Key.OemComma => 27, Key.OemPeriod => 28, Key.OemQuestion => 29,
            _ => -1
        };
        return index < 0 ? 0 : index + 1;
    }

    /// <summary>数字键 1..0 -> 分组位次 0..9，不匹配返回 -1。</summary>
    public static int GroupIndexFromKey(Key key) => key switch
    {
        Key.D1 => 0, Key.D2 => 1, Key.D3 => 2, Key.D4 => 3, Key.D5 => 4,
        Key.D6 => 5, Key.D7 => 6, Key.D8 => 7, Key.D9 => 8, Key.D0 => 9,
        _ => -1
    };
}

/// <summary>界面上的一个磁贴。</summary>
public sealed class TileItem : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public TileItem(AppEntry entry, AppGroup? owner)
    {
        Entry = entry;
        Owner = owner;
        KeyLabel = entry.FunctionKey > 0
            ? "F" + entry.FunctionKey
            : entry.Slot > 0 ? SlotKeys.Label(entry.Slot) : "";
        DisplayName = string.IsNullOrWhiteSpace(entry.Name)
            ? System.IO.Path.GetFileNameWithoutExtension(entry.Target)
            : entry.Name;
        Tip = string.IsNullOrWhiteSpace(entry.Target) ? DisplayName : entry.Target;
    }

    public AppEntry Entry { get; }
    public AppGroup? Owner { get; }
    public string DisplayName { get; }
    public string KeyLabel { get; }
    public bool HasKey => KeyLabel.Length > 0;
    public string Tip { get; }

    public bool IsFavorite => Entry.FunctionKey > 0;

    private int _iconLoadState;

    /// <summary>标记"已排入图标提取队列"，返回 false 表示无需再排（已有图标或已在队列中）。</summary>
    public bool TryBeginIconLoad() => _icon == null && Interlocked.CompareExchange(ref _iconLoadState, 1, 0) == 0;

    /// <summary>提取失败/取消：允许下次重建时重试。</summary>
    public void CancelIconLoad() => Interlocked.Exchange(ref _iconLoadState, 0);

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// 键盘网格里的一个格位：可能绑定条目，也可能是空位（保留键位对齐）。
/// Tile 可变：切组/编辑后只更新内容，复用同一个容器，避免整个网格重建。
/// </summary>
public sealed class SlotCell : INotifyPropertyChanged
{
    private TileItem? _tile;

    public SlotCell(int slot, TileItem? tile)
    {
        Slot = slot;
        _tile = tile;
    }

    public int Slot { get; }
    public string KeyLabel => SlotKeys.Label(Slot);

    public TileItem? Tile
    {
        get => _tile;
        set
        {
            if (ReferenceEquals(_tile, value)) return;
            _tile = value;
            Raise(nameof(Tile));
            Raise(nameof(HasTile));
        }
    }

    public bool HasTile => _tile is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>收藏栏里的一个 F 键格位：F1–F12 固定占位，Tile 可变（同上）。</summary>
public sealed class FavSlot : INotifyPropertyChanged
{
    private TileItem? _tile;

    public FavSlot(int functionKey, TileItem? tile)
    {
        FunctionKey = functionKey;
        _tile = tile;
    }

    public int FunctionKey { get; }
    public string KeyLabel => "F" + FunctionKey;

    public TileItem? Tile
    {
        get => _tile;
        set
        {
            if (ReferenceEquals(_tile, value)) return;
            _tile = value;
            Raise(nameof(Tile));
            Raise(nameof(HasTile));
        }
    }

    public bool HasTile => _tile is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GroupPill : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _name;

    public GroupPill(int index, string name)
    {
        Index = index;
        _name = name;
    }

    public int Index { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
