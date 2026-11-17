using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickBoard.Core;
using QuickBoard.Views;

namespace QuickBoard;

public partial class MainWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOMOVE = 0x0002;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12;
    private const int HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    // 强制居中需要：从 WINDOWPOS 读/写物理像素坐标，并取当前显示器的工作区
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    private readonly ConfigStore _store;
    private readonly BoardConfig _config;
    private readonly ObservableCollection<SlotCell> _slots = new();
    private readonly ObservableCollection<FavSlot> _favorites = new();
    private readonly ObservableCollection<TileItem> _extras = new();
    private readonly ObservableCollection<GroupPill> _groups = new();
    private readonly HotkeyService _hotkeys = new();

    private int _groupIndex;
    private int _suppressHide;
    private string? _hotkeyError;
    private int _wheelDelta;
    private bool _resizeDragging;
    private Point _resizeStartPoint;
    private Size _resizeStartSize;

    /// <summary>条目剪贴板（剪切/复制后等待粘贴）。null = 空。</summary>
    private ClipPiece? _clip;

    public MainWindow(ConfigStore store, BoardConfig config)
    {
        InitializeComponent();

        _store = store;
        _config = config;

        IconService.Initialize(store.IconCacheDirectory);

        SlotGrid.ItemsSource = _slots;
        FavoritesBar.ItemsSource = _favorites;
        ExtraGrid.ItemsSource = _extras;
        GroupBar.ItemsSource = _groups;

        Topmost = config.Options.AlwaysOnTop;
        ApplyWindowRect();
        ApplyAppearance();

        EnsureGroups();
        RebuildGroupBar();
        SelectGroup(ResolveInitialGroup());

        _hotkeys.Pressed += ToggleVisible;

        // 提前建好窗口句柄，这样即使以「启动时隐藏」方式运行，全局热键也能生效
        new WindowInteropHelper(this).EnsureHandle();
        _hotkeyError = _hotkeys.Register(this, _config.Hotkey);

        ResizeGrip.MouseMove += Grip_MouseMove;
        ResizeGrip.MouseLeftButtonUp += Grip_MouseUp;
        IsVisibleChanged += (_, _) => { if (!IsVisible) PersistWindowRect(); };
        Loaded += OnLoaded;
    }

    // ---------- 分组 ----------

    private AppGroup? CurrentGroup =>
        _groupIndex >= 0 && _groupIndex < _config.Groups.Count ? _config.Groups[_groupIndex] : null;

    private AppGroup? FavoritesGroup => _config.Groups.FirstOrDefault(g => g.IsFavorites);

    public string CurrentGroupName => CurrentGroup?.Name ?? "-";

    /// <summary>离屏探针/预览时置 true，避免把窗口位置写回真实配置。</summary>
    public bool SuppressPersist { get; set; }

    private List<int> SelectableOrder => GroupRules.SelectableIndices(_config);

    private void EnsureGroups()
    {
        if (!_config.Groups.Any(g => !g.IsFavorites))
            _config.Groups.Insert(0, new AppGroup { Name = "常用" });

        // 至少保留一个可见分组，否则界面无组可选
        if (!_config.Groups.Any(g => !g.IsFavorites && !g.IsHidden))
            _config.Groups.First(g => !g.IsFavorites).IsHidden = false;

        // 主界面分组若被删除/隐藏/改成收藏栏则清空，避免指向失效 Id
        if (!string.IsNullOrWhiteSpace(_config.HomeGroupId) && !GroupRules.IsHomeValid(_config))
            _config.HomeGroupId = "";
    }

    private AppGroup EnsureFavoritesGroup()
    {
        var group = FavoritesGroup;
        if (group is not null) return group;

        group = new AppGroup { Name = "收藏栏", IsFavorites = true };
        _config.Groups.Add(group);   // 追加在末尾，避免打乱已有分组索引
        return group;
    }

    private int ResolveInitialGroup() => GroupRules.ResolveInitialIndex(_config);

    private void RebuildGroupBar()
    {
        _groups.Clear();
        foreach (var index in SelectableOrder)
        {
            var group = _config.Groups[index];
            // 主界面分组加 ★ 前缀，用户在分组栏一眼可辨
            string label = group.Id == _config.HomeGroupId ? "★ " + group.Name : group.Name;
            _groups.Add(new GroupPill(index, label));
        }
    }

    private void SelectGroup(int index)
    {
        var order = SelectableOrder;
        if (order.Count == 0) return;

        _groupIndex = order.Contains(index) ? index : order[0];
        _config.LastGroupIndex = _groupIndex;

        foreach (var pill in _groups) pill.IsSelected = pill.Index == _groupIndex;

        RebuildBoard();
    }

    /// <summary>滚轮 / 数字键换组，首尾循环。</summary>
    private void StepGroup(int direction)
    {
        var order = SelectableOrder;
        if (order.Count <= 1) return;

        int pos = order.IndexOf(_groupIndex);
        if (pos < 0) pos = 0;
        int next = (pos + direction % order.Count + order.Count) % order.Count;
        SelectGroup(order[next]);
    }

    private void SelectGroupByPosition(int position)
    {
        var order = SelectableOrder;
        if (position >= 0 && position < order.Count) SelectGroup(order[position]);
    }

    // ---------- 显示 / 隐藏 ----------

    private void ShadowLayer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateShadow();

    private double _shadowWidth, _shadowHeight;

    /// <summary>
    /// 生成面板柔和投影（尺寸变化时重算一次）。
    /// 不再用 DropShadowEffect：分层窗口下 WPF 要为整块面板分配多张软件渲染中间位图，
    /// 实测多占约 48MB；换成一张几十万像素的位图后，观感一致但内存只剩约 0.7MB。
    /// </summary>
    private void UpdateShadow()
    {
        double w = ShadowLayer.ActualWidth;
        double h = ShadowLayer.ActualHeight;
        if (w < 40 || h < 40) return;
        if (Math.Abs(w - _shadowWidth) < 0.5 && Math.Abs(h - _shadowHeight) < 0.5) return;

        _shadowWidth = w;
        _shadowHeight = h;

        // 面板四周留 14（与 RootBorder.Margin 一致），圆角 14，柔和度按可用空间取，避免阴影被边界硬切
        ShadowLayer.Background = Core.SoftShadow.Create(w, h, padding: 14, radius: 14,
            blur: 8, offsetY: 4, opacity: 0.55);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateShadow();

        if (_hotkeyError is not null)
        {
            StatusText.Text = _hotkeyError;
        }

        HintText.Text = "1–0 分组 · 滚轮换组 · Q–/ 启动 · F1–F12 收藏";

        // 强制居中：首次显示后立刻钉到工作区中央（WndProc 已兜底所有后续位置变更）
        if (_config.Options.CenterOnScreen) Dispatcher.BeginInvoke(new Action(CenterOnScreen));
    }

    public void ShowAndFocus()
    {
        if (!IsVisible) Show();
        if (_config.Options.CenterOnScreen) CenterOnScreen();

        Topmost = _config.Options.AlwaysOnTop;
        Activate();
        RootBorder.Focus();
        Keyboard.Focus(RootBorder);

        RebuildBoard();
    }

    public void ToggleVisible()
    {
        if (IsVisible && IsActive) Hide();
        else ShowAndFocus();
    }

    private void CenterOnScreen()
    {
        // 用当前显示器工作区（与 WndProc 的强制居中保持一致），单位 DIP
        var area = SystemParameters.WorkArea;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                // WndProc 拿到的是物理像素；这里换算回 DIP 供 WPF 的 Left/Top 使用
                var source = PresentationSource.FromVisual(this)?.CompositionTarget;
                double sx = source?.TransformToDevice.M11 ?? 1;
                double sy = source?.TransformToDevice.M22 ?? 1;
                if (sx > 0 && sy > 0)
                {
                    double wDip = (mi.rcWork.Right - mi.rcWork.Left) / sx;
                    double hDip = (mi.rcWork.Bottom - mi.rcWork.Top) / sy;
                    double lDip = mi.rcWork.Left / sx;
                    double tDip = mi.rcWork.Top / sy;
                    Left = lDip + (wDip - ActualWidth) / 2;
                    Top = tDip + (hDip - ActualHeight) / 2;
                    return;
                }
            }
        }
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    }

    private void ApplyWindowRect()
    {
        var o = _config.Options;

        Width = Math.Max(MinWidth, o.WindowWidth > 0 ? o.WindowWidth : 1180);
        Height = Math.Max(MinHeight, o.WindowHeight > 0 ? o.WindowHeight : 660);

        if (o.HasWindowPosition && !o.CenterOnScreen)
        {
            Left = o.WindowLeft;
            Top = o.WindowTop;
        }
    }

    private void PersistWindowRect()
    {
        if (SuppressPersist) return;

        var o = _config.Options;
        if (WindowState == WindowState.Normal && ActualWidth > 200 && ActualHeight > 150)
        {
            o.WindowWidth = ActualWidth;
            o.WindowHeight = ActualHeight;
            o.WindowLeft = Left;
            o.WindowTop = Top;
            o.HasWindowPosition = true;
        }

        Save();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_config.Options.HideOnDeactivate) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive && _suppressHide == 0) Hide();
        }), DispatcherPriority.Background);
    }

    /// <summary>
    /// 打开对话框 / 弹出菜单期间抑制「失焦自动隐藏」。
    /// 用于 using 块；菜单这种跨事件配对的场景用 BeginSuppressHide / EndSuppressHide。
    /// </summary>
    private IDisposable SuppressAutoHide() => new HideSuppressor(this);

    private void BeginSuppressHide() => _suppressHide++;

    private void EndSuppressHide()
    {
        if (_suppressHide > 0) _suppressHide--;
    }

    private sealed class HideSuppressor : IDisposable
    {
        private MainWindow? _owner;

        public HideSuppressor(MainWindow owner)
        {
            _owner = owner;
            owner._suppressHide++;
        }

        public void Dispose()
        {
            if (_owner is null) return;
            _owner.EndSuppressHide();
            _owner = null;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        PersistWindowRect();
        Hide();
    }

    // ---------- 外观 ----------

    private void ApplyAppearance() => ApplyAppearance(_config.Options);

    private void ApplyAppearance(BoardOptions o)
    {
        Appearance.Apply(o.UiOpacity, o.ThemeKind, o.IconScale, o.NameFontSize);

        // 实测元素 Opacity 并不带来额外合成层开销（与画刷透明度内存相同），
        // 但画刷透明度会在设置里拖动滑块时反复重新解码背景图，故仍用元素透明度。
        var brush = Appearance.LoadImageBrush(o.BackgroundImage, o.BackgroundFit,
            o.BackgroundCropX, o.BackgroundCropY, o.BackgroundCropWidth, o.BackgroundCropHeight);
        if (brush is null)
        {
            PhotoBrush.Background = null;
            PhotoBrush.Opacity = 0;
            PhotoScrim.Opacity = 0;
        }
        else
        {
            PhotoBrush.Background = brush;
            PhotoBrush.Opacity = Math.Clamp(o.BackgroundImageOpacity, 0, 1);
            PhotoScrim.Opacity = Math.Clamp(o.BackgroundImageOpacity, 0, 1);
        }
    }

    // ---------- 渲染 ----------

    private void RebuildBoard()
    {
        var group = CurrentGroup;
        var favorites = FavoritesGroup;

        // 收藏栏：F1–F12 固定 12 个容器，只更新内容（切组/保存后不重建视觉树）
        EnsureFavSlots();
        if (favorites is not null && _config.Options.ShowFavoritesBar)
        {
            // 按 F 键号建索引：F1–F12 全部占位，有条目的填进去，空位保留 F 键标签
            var byKey = new Dictionary<int, AppEntry>();
            foreach (var entry in favorites.Entries)
                if (entry.FunctionKey >= 1 && entry.FunctionKey <= 12 && !byKey.ContainsKey(entry.FunctionKey))
                    byKey[entry.FunctionKey] = entry;

            for (int fk = 1; fk <= 12; fk++)
                _favorites[fk - 1].Tile = byKey.TryGetValue(fk, out var entry) ? NewTile(entry, favorites) : null;
        }
        else
        {
            foreach (var slot in _favorites) slot.Tile = null;
        }

        FavPanel.Visibility = _config.Options.ShowFavoritesBar ? Visibility.Visible : Visibility.Collapsed;
        FavHint.Visibility = _favorites.Any(x => x.HasTile) ? Visibility.Collapsed : Visibility.Visible;

        // 键盘区：固定 30 格（三排 × 十列），同样只更新内容
        EnsureSlotCells();

        var bySlot = new Dictionary<int, AppEntry>();
        var extras = new List<AppEntry>();

        foreach (var entry in group?.Entries ?? Enumerable.Empty<AppEntry>())
        {
            if (entry.Slot >= 1 && entry.Slot <= SlotKeys.Count && !bySlot.ContainsKey(entry.Slot))
                bySlot[entry.Slot] = entry;
            else
                extras.Add(entry);
        }

        for (int slot = 1; slot <= SlotKeys.Count; slot++)
            _slots[slot - 1].Tile = bySlot.TryGetValue(slot, out var entry) ? NewTile(entry, group!) : null;

        _extras.Clear();
        foreach (var entry in extras.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            AddTile(_extras, entry, group!);

        ExtraHint.Text = $"另有 {_extras.Count} 个条目没有键位（30 个键位已满），滚到下方点击即可启动";
        ExtraHint.Visibility = _extras.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyHint.Text = "这个分组还是空的 —— 把文件拖进来，或点右上角「添加」";
        EmptyHint.Visibility = (group?.Entries.Count ?? 0) == 0 ? Visibility.Visible : Visibility.Collapsed;

        KeyboardScroller.ScrollToTop();

        UpdateStatus();
    }

    /// <summary>补齐 30 个键盘格位容器（只补缺，不重建已有容器）。</summary>
    private void EnsureSlotCells()
    {
        for (int slot = _slots.Count + 1; slot <= SlotKeys.Count; slot++)
            _slots.Add(new SlotCell(slot, null));
    }

    /// <summary>补齐 12 个收藏格位容器（只补缺，不重建已有容器）。</summary>
    private void EnsureFavSlots()
    {
        for (int fk = _favorites.Count + 1; fk <= 12; fk++)
            _favorites.Add(new FavSlot(fk, null));
    }

    private TileItem NewTile(AppEntry entry, AppGroup owner)
    {
        var item = new TileItem(entry, owner);
        // 图标走单线程队列：不再为每个条目创建 Task.Run（会打爆线程池）
        IconLoader.Enqueue(item);
        return item;
    }

    private void AddTile(ICollection<TileItem> target, AppEntry entry, AppGroup owner) =>
        target.Add(NewTile(entry, owner));

    private void UpdateStatus()
    {
        var group = CurrentGroup;
        var total = _config.Groups.Where(g => !g.IsFavorites).Sum(g => g.Entries.Count);
        StatusText.Text = $"{group?.Name ?? "-"} · 本组 {group?.Entries.Count ?? 0} 个 · 全部 {total} 个 · 热键 {_config.Hotkey}";
    }

    private void Save()
    {
        // 探针/离屏实例一律不落盘：否则会污染用户的 board.json
        if (SuppressPersist) return;
        _store.Save(_config);
    }

    /// <summary>
    /// 注册全局热键；失败时 HotkeyService 会回滚到上一次可用的组合，
    /// 这里把配置里的热键也一并回滚，避免下次启动继续用无效组合。
    /// </summary>
    private void ApplyHotkey()
    {
        var error = _hotkeys.Register(this, _config.Hotkey);
        if (error is null) return;

        StatusText.Text = error;
        if (_hotkeys.Active is { } active && !SameHotkey(active, _config.Hotkey))
        {
            _config.Hotkey = active;
            Save();
            UpdateStatus();
            StatusText.Text = error;
        }
    }

    private static bool SameHotkey(HotkeySetting a, HotkeySetting b) =>
        a.Ctrl == b.Ctrl && a.Alt == b.Alt && a.Shift == b.Shift && a.Win == b.Win &&
        string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);

    // ---------- 启动 ----------

    private void LaunchItem(TileItem? item)
    {
        if (item is null) return;
        LaunchEntry(item.Entry);
    }

    private void LaunchEntry(AppEntry entry)
    {
        var result = Launcher.Launch(entry);
        if (!result.Ok)
        {
            StatusText.Text = "启动失败：" + result.Error;
            return;
        }

        if (_config.Options.HideAfterLaunch) Hide();
    }

    // ---------- 键盘 ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            return;

        if (e.Key >= Key.F1 && e.Key <= Key.F12)
        {
            int fk = (int)e.Key - (int)Key.F1 + 1;
            var favorites = FavoritesGroup;
            var target = favorites?.Entries.FirstOrDefault(x => x.FunctionKey == fk)
                         ?? _config.Groups.SelectMany(g => g.Entries).FirstOrDefault(x => x.FunctionKey == fk);
            if (target is not null)
            {
                LaunchEntry(target);
                e.Handled = true;
            }

            return;
        }

        int groupPosition = SlotKeys.GroupIndexFromKey(e.Key);
        if (groupPosition >= 0)
        {
            SelectGroupByPosition(groupPosition);
            e.Handled = true;
            return;
        }

        int slot = SlotKeys.FromKey(e.Key);
        if (slot > 0)
        {
            var entry = CurrentGroup?.Entries.FirstOrDefault(x => x.Slot == slot && x.FunctionKey == 0)
                        ?? CurrentGroup?.Entries.FirstOrDefault(x => x.Slot == slot);
            if (entry is not null)
            {
                LaunchEntry(entry);
                e.Handled = true;
            }
        }
    }

    /// <summary>鼠标滚轮切换分组：列表本身可滚动时先让列表滚。</summary>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0) return;

        if (KeyboardScroller.IsMouseOver && KeyboardScroller.ScrollableHeight > 0.5) return;

        _wheelDelta += e.Delta;
        if (Math.Abs(_wheelDelta) < 120)
        {
            e.Handled = true;
            return;
        }

        int direction = _wheelDelta > 0 ? -1 : 1;
        _wheelDelta = 0;
        StepGroup(direction);
        e.Handled = true;
    }

    // ---------- 窗口缩放（无边框，自己处理命中测试）----------

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 强制居中：拦截所有位置变更（拖动 / 缩放 / 系统自动），把左上角钉在当前显示器工作区中央。
        // 只改 x/y，保留 cx/cy，因此 8 向缩放照常工作，只是缩放后窗口仍保持居中。
        // 关闭 CenterOnScreen 时完全放行，恢复原来自由拖动的行为。
        if (msg == WM_WINDOWPOSCHANGING && _config.Options.CenterOnScreen
            && WindowState == WindowState.Normal && lParam != IntPtr.Zero)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            // SWP_NOSIZE 时 cx/cy 可能为 0，回退到窗口当前实际尺寸；
            // 早期创建消息（窗口矩形也无效）则跳过，避免算出错误中心。
            int useCx = wp.cx, useCy = wp.cy;
            if (useCx <= 0 || useCy <= 0)
            {
                var cur = new RECT();
                if (GetWindowRect(hwnd, ref cur))
                {
                    useCx = cur.Right - cur.Left;
                    useCy = cur.Bottom - cur.Top;
                }
            }
            if (useCx <= 0 || useCy <= 0) return IntPtr.Zero;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                int centerX = mi.rcWork.Left + (mi.rcWork.Right - mi.rcWork.Left - useCx) / 2;
                int centerY = mi.rcWork.Top + (mi.rcWork.Bottom - mi.rcWork.Top - useCy) / 2;
                if (wp.x != centerX || wp.y != centerY)
                {
                    wp.x = centerX;
                    wp.y = centerY;
                    wp.flags &= ~SWP_NOMOVE;   // 让系统按新坐标真正移动
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            // 不设 handled：让系统继续按改过的 WINDOWPOS 处理
            return IntPtr.Zero;
        }

        // 面板外围设了 HTCAPTION，双击会触发系统最大化 —— 启动器保持常规尺寸，直接吞掉
        if (msg == WM_NCLBUTTONDBLCLK)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WM_NCHITTEST || WindowState != WindowState.Normal) return IntPtr.Zero;

        long lp = lParam.ToInt64();
        int screenX = (short)(lp & 0xFFFF);
        int screenY = (short)((lp >> 16) & 0xFFFF);

        Point point;
        try { point = PointFromScreen(new Point(screenX, screenY)); }
        catch { return IntPtr.Zero; }

        int code = HitTestResize(point);
        if (code == 0) return IntPtr.Zero;

        handled = true;
        return new IntPtr(code);
    }

    /// <summary>
    /// 可见面板（RootBorder）的边缘判定：±7px 是 8 向缩放手柄，面板外围透明边按住可拖动窗口，
    /// 面板内部返回 0（交给 WPF 正常处理，按钮/磁贴才点得到）。
    /// </summary>
    private int HitTestResize(Point point)
    {
        Rect panel;
        try
        {
            var tl = RootBorder.TranslatePoint(new Point(0, 0), this);
            var br = RootBorder.TranslatePoint(new Point(RootBorder.ActualWidth, RootBorder.ActualHeight), this);
            panel = new Rect(tl, br);
        }
        catch
        {
            panel = Rect.Empty;
        }

        if (panel.IsEmpty || panel.Width < 50 || panel.Height < 50)
            panel = new Rect(0, 0, ActualWidth, ActualHeight);   // 布局未就绪：退回整窗边缘

        const double edge = 7;
        bool left = point.X <= panel.Left + edge;
        bool right = point.X >= panel.Right - edge;
        bool top = point.Y <= panel.Top + edge;
        bool bottom = point.Y >= panel.Bottom - edge;

        if (left && top) return HTTOPLEFT;
        if (right && top) return HTTOPRIGHT;
        if (left && bottom) return HTBOTTOMLEFT;
        if (right && bottom) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;

        bool outsidePanel = point.X < panel.Left || point.X > panel.Right
                            || point.Y < panel.Top || point.Y > panel.Bottom;
        return outsidePanel ? HTCAPTION : 0;   // 面板外围透明边框：拖动整个窗口
    }

    private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _resizeDragging = true;
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartSize = new Size(ActualWidth, ActualHeight);
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizeDragging) return;

        var point = e.GetPosition(this);
        Width = Math.Max(MinWidth, _resizeStartSize.Width + (point.X - _resizeStartPoint.X));
        Height = Math.Max(MinHeight, _resizeStartSize.Height + (point.Y - _resizeStartPoint.Y));
    }

    private void Grip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizeDragging) return;
        _resizeDragging = false;
        ResizeGrip.ReleaseMouseCapture();
        PersistWindowRect();
    }

    // ---------- 交互事件 ----------

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // 强制居中时窗口不动（拖动交给 WndProc 拦截钉回中央）；关闭居中才允许自由拖动
        if (_config.Options.CenterOnScreen) return;
        try { DragMove(); } catch { }
    }

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            SelectGroup(index);
            RootBorder.Focus();
        }
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TileItem item }) LaunchItem(item);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

    private void BtnSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void MenuSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void BtnGroups_Click(object sender, RoutedEventArgs e) => OpenGroupEditor();

    /// <summary>分组右键菜单「编辑分组…」：进入统一的分组编辑页。</summary>
    private void MenuEditGroup_Click(object sender, RoutedEventArgs e) => OpenGroupEditor();

    /// <summary>统一的分组编辑入口：新增 / 删除 / 隐藏 / 设为主界面 / 重命名 / 排序。</summary>
    public void OpenGroupEditor()
    {
        using var hideGuard = SuppressAutoHide();

        var dialog = new GroupEditorDialog(_config) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        EnsureGroups();
        RebuildGroupBar();

        // 当前分组若被删除或隐藏，回退到主界面分组（或第一个可选分组）
        SelectGroup(!SelectableOrder.Contains(_groupIndex)
            ? GroupRules.ResolveInitialIndex(_config)
            : _groupIndex);

        Save();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            AddPaths(files);
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        using var hideGuard = SuppressAutoHide();

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加的程序或快捷方式",
            Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }

    public void OpenSettings()
    {
        using var hideGuard = SuppressAutoHide();

        var dialog = new SettingsDialog(_config, _store, ApplyAppearance, _hotkeyError) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            ApplyAppearance();   // 取消：把预览过的外观还原（热键/导入均为暂存，无需回滚）
            return;
        }

        _config.Options = dialog.Options;
        Save();
        ApplyAppearance();
        Topmost = _config.Options.AlwaysOnTop;

        ApplyHotkey();

        if (dialog.GroupsChanged)
        {
            _groupIndex = 0;
            EnsureGroups();
        }

        RebuildGroupBar();
        SelectGroup(ResolveInitialGroup());
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        BeginSuppressHide();

        if (sender is not ContextMenu menu) return;

        // 分组胶囊菜单（Tag 是分组索引）：全部可用
        if (menu.PlacementTarget is Button { Tag: int }) return;

        bool hasTile = menu.PlacementTarget is Button { Tag: TileItem };
        bool canPaste = _clip is not null;

        // 空键位/空收藏位：只留「编辑…」（新建）和「粘贴」（剪贴板非空时），其余淡色禁用
        foreach (var mi in menu.Items.OfType<MenuItem>())
        {
            var header = mi.Header as string ?? "";
            bool paste = header == "粘贴";
            mi.IsEnabled = hasTile ? (!paste || canPaste) : (header == "编辑…" || (paste && canPaste));
        }
    }

    private void MenuLaunch_Click(object sender, RoutedEventArgs e) => LaunchItem(TagOf(sender));

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        var item = TagOf(sender);

        // 空键位上的「编辑…」= 新建一个绑定该键位的条目（与原版行为一致）
        if (item is null)
        {
            EditEmptySlot(sender);
            return;
        }

        using var hideGuard = SuppressAutoHide();

        var dialog = new EditEntryDialog(item.Entry) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        ApplyFavoritePlacement(item);
        Save();
        RebuildBoard();
    }

    /// <summary>空键位上点「编辑…」：弹出编辑框新建条目，保存后落到当前分组（或收藏栏）。
    /// 空收藏位（FavSlot）预填对应 F 键号，保存后直接进收藏栏。</summary>
    private void EditEmptySlot(object sender)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: Button btn } })
            return;

        int slot = 0, functionKey = 0;
        switch (btn.DataContext)
        {
            case SlotCell cell:
                slot = cell.Slot;
                break;
            case FavSlot fav:
                functionKey = fav.FunctionKey;
                break;
            default:
                return;
        }

        using var hideGuard = SuppressAutoHide();

        var entry = new AppEntry { Slot = slot, FunctionKey = functionKey };
        var dialog = new EditEntryDialog(entry) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (string.IsNullOrWhiteSpace(entry.Target))
        {
            StatusText.Text = "未填写目标路径，已放弃添加";
            return;
        }

        // 新建条目落到哪个分组，与「编辑已有条目」共用同一套迁移规则
        SyncEntryPlacement(entry, entry.FunctionKey > 0);

        Save();
        RebuildBoard();
    }

    /// <summary>
    /// 条目归属的唯一判定点：把它放到收藏栏还是当前分组。
    /// - wantsFavorite=true：进收藏栏（同 F 键号旧条目让位，不占字母键位；12 位已满则退回主分组）
    /// - wantsFavorite=false：留在/回到当前分组，并补一个空闲字母键位
    /// </summary>
    private void SyncEntryPlacement(AppEntry entry, bool wantsFavorite, AppGroup? origin = null)
    {
        var favorites = FavoritesGroup;
        bool inFavorites = favorites?.Entries.Contains(entry) == true;
        origin ??= _config.Groups.FirstOrDefault(g => g.Entries.Contains(entry));

        if (wantsFavorite)
        {
            var target = EnsureFavoritesGroup();
            int functionKey = entry.FunctionKey > 0 ? entry.FunctionKey : NextFreeFunctionKey(target);
            if (functionKey > 0)
            {
                var clash = target.Entries.FirstOrDefault(x => !ReferenceEquals(x, entry) && x.FunctionKey == functionKey);
                if (clash is not null) target.Entries.Remove(clash);

                entry.FunctionKey = functionKey;
                entry.Slot = 0;
                if (!inFavorites)
                {
                    origin?.Entries.Remove(entry);
                    target.Entries.Add(entry);
                }

                return;
            }

            // 12 个收藏位全满：退回主分组，不留下 F 键号 0 的"隐形收藏条目"
            entry.FunctionKey = 0;
        }

        if (inFavorites)
        {
            favorites!.Entries.Remove(entry);
            entry.FunctionKey = 0;
        }

        var destination = CurrentGroup is { IsFavorites: false } current
            ? current
            : _config.Groups.FirstOrDefault(g => !g.IsFavorites);

        if (destination is null) return;
        if (!destination.Entries.Contains(entry)) destination.Entries.Add(entry);
        if (entry.Slot <= 0) entry.Slot = NextFreeSlot(destination);
    }

    /// <summary>编辑里改了「收藏键」后，把条目搬到正确的分组（主网格 / 收藏栏）。</summary>
    private void ApplyFavoritePlacement(TileItem item) =>
        SyncEntryPlacement(item.Entry, item.Entry.FunctionKey > 0, item.Owner);

    private void MenuOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var item = TagOf(sender);
        if (item is not null) ShortcutResolver.OpenContainingFolder(item.Entry.Target);
    }

    private void MenuToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        var item = TagOf(sender);
        if (item is null) return;

        bool inFavorites = FavoritesGroup?.Entries.Contains(item.Entry) == true;

        // 加入 / 移出收藏栏走同一套归属规则
        SyncEntryPlacement(item.Entry, !inFavorites, item.Owner);
        RebuildGroupBar();   // 收藏组可能刚被创建，分组栏需要刷新

        Save();
        SelectGroup(_groupIndex);
    }

    private static int NextFreeFunctionKey(AppGroup favorites) => EntryClipboard.NextFreeFunctionKey(favorites);

    // ---------- 剪切 / 复制 / 粘贴 / 删除（右键直接完成，不必进编辑对话框）----------

    private void MenuCut_Click(object sender, RoutedEventArgs e) => SetClipboard(TagOf(sender), cut: true);

    private void MenuCopy_Click(object sender, RoutedEventArgs e) => SetClipboard(TagOf(sender), cut: false);

    private void SetClipboard(TileItem? item, bool cut)
    {
        if (item is null) return;

        var piece = EntryClipboard.Create(item.Entry, item.Owner ?? OwnerOf(item.Entry), cut);
        if (piece is null)
        {
            StatusText.Text = "这个条目还没有目标，无法剪切或复制";
            return;
        }

        _clip = piece;
        StatusText.Text = cut
            ? $"已剪切「{piece.DisplayName}」：到目标位置右键 → 粘贴即可移动"
            : $"已复制「{piece.DisplayName}」：可以连续粘贴多次";
    }

    private void MenuPaste_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null)
        {
            StatusText.Text = "剪贴板里没有条目";
            return;
        }

        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: Button btn } }) return;
        if (!TryResolveTarget(btn, out var group, out int slot, out int functionKey) || group is null) return;

        using var hideGuard = SuppressAutoHide();   // 替换确认框是模态，别让它触发自动隐藏

        var outcome = EntryClipboard.Paste(_config, _clip, group, slot, functionKey, occupant =>
            MessageBox.Show(this,
                $"键位 {EntryClipboard.LabelOf(slot, functionKey)} 已被「{EntryClipboard.DisplayName(occupant)}」占用，替换它吗？",
                "粘贴", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

        switch (outcome)
        {
            case PasteOutcome.NoTarget:
                StatusText.Text = "这个位置放不下（收藏位只接 F 键，字母键位需要 1–30 号）";
                return;
            case PasteOutcome.Cancelled:
                StatusText.Text = "已取消粘贴";
                return;
            case PasteOutcome.AlreadyHere:
                StatusText.Text = "条目已经在这个位置了";
                return;
        }

        if (_clip.IsCut) _clip = null;   // 剪切只生效一次；复制可以继续粘

        RebuildGroupBar();               // 粘到收藏栏时收藏组可能刚被创建
        Save();
        SelectGroup(_groupIndex);
        StatusText.Text = outcome switch
        {
            PasteOutcome.Swapped => "已互换两个条目的键位",
            PasteOutcome.Replaced => "已替换该键位上的条目",
            PasteOutcome.Moved => "已移动到该键位",
            _ => "已粘贴到该键位"
        };
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = TagOf(sender);
        if (item?.Owner is null) return;

        var name = item.DisplayName;
        item.Owner.Entries.Remove(item.Entry);
        if (ReferenceEquals(_clip?.Entry, item.Entry)) _clip = null;

        Save();
        RebuildBoard();
        StatusText.Text = $"已删除「{name}」";
    }

    /// <summary>右键落点 → 粘贴目标：字母键位取 SlotCell，收藏位取 FavSlot，溢出区取条目自身位置。</summary>
    private bool TryResolveTarget(Button btn, out AppGroup? group, out int slot, out int functionKey)
    {
        group = null;
        slot = 0;
        functionKey = 0;

        switch (btn.DataContext)
        {
            case SlotCell cell:
                group = CurrentGroup;
                slot = cell.Slot;
                break;
            case FavSlot fav:
                group = FavoritesGroup ?? EnsureFavoritesGroup();
                functionKey = fav.FunctionKey;
                break;
            case TileItem tile:
                group = tile.Owner ?? OwnerOf(tile.Entry);
                slot = tile.Entry.Slot;
                functionKey = tile.Entry.FunctionKey;
                break;
            default:
                return false;
        }

        return group is not null;
    }

    private AppGroup? OwnerOf(AppEntry entry) =>
        _config.Groups.FirstOrDefault(g => g.Entries.Contains(entry));

    private void ContextMenu_Closed(object sender, RoutedEventArgs e) => EndSuppressHide();

    private static TileItem? TagOf(object sender) =>
        (sender as FrameworkElement)?.Tag as TileItem;

    private void AddPaths(IEnumerable<string> paths)
    {
        var group = CurrentGroup;
        if (group is null) return;

        int added = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            string name;
            if (File.Exists(path)) name = Path.GetFileNameWithoutExtension(path);
            else if (Directory.Exists(path)) name = new DirectoryInfo(path).Name;
            else continue;

            group.Entries.Add(new AppEntry
            {
                Name = string.IsNullOrWhiteSpace(name) ? path : name,
                Target = path,
                WorkingDirectory = File.Exists(path) ? Path.GetDirectoryName(path) ?? "" : "",
                Slot = NextFreeSlot(group)
            });
            added++;
        }

        if (added == 0) return;

        Save();
        RebuildBoard();
        StatusText.Text = $"已添加 {added} 个条目到「{group.Name}」";
    }

    private static int NextFreeSlot(AppGroup group) => EntryClipboard.NextFreeSlot(group);

    /// <summary>探针用：按位次切组，验证增量刷新（容器复用、内容更新）。</summary>
    public void DebugSelectGroup(int position) => SelectGroupByPosition(position);

    /// <summary>探针用：全部条目数（跨分组求和），用于验证剪切/粘贴这类会改变数量的操作。</summary>
    public int DebugEntryCount() => _config.Groups.Sum(g => g.Entries.Count);

    /// <summary>探针用：剪贴板当前是否持有条目。</summary>
    public bool DebugHasClip => _clip is not null;

    public void ReloadFromDisk()    {
        var fresh = _store.Load();
        _clip = null;   // 重载后旧对象已不在配置里，剪贴板必须作废，否则粘贴会插回旧条目
        _config.Version = fresh.Version;
        _config.Groups = fresh.Groups;
        _config.Hotkey = fresh.Hotkey;
        _config.Options = fresh.Options;
        _config.HomeGroupId = fresh.HomeGroupId;      // 主界面分组：漏复制会导致重载后回落到 LastGroupIndex
        _config.LastGroupIndex = fresh.LastGroupIndex;
        _groupIndex = 0;

        ApplyWindowRect();
        ApplyAppearance();
        Topmost = _config.Options.AlwaysOnTop;

        EnsureGroups();
        RebuildGroupBar();
        SelectGroup(ResolveInitialGroup());

        // 重载后热键可能已变化：重新注册（失败时回滚到旧组合并在状态栏提示）
        ApplyHotkey();
    }
}
