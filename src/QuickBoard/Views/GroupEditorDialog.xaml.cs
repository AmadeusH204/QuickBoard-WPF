using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickBoard.Core;

namespace QuickBoard.Views;

/// <summary>分组编辑页的一行。Name 直接写回克隆的 AppGroup，取消时不影响真实配置。</summary>
public sealed class GroupEditRow
{
    public GroupEditRow(AppGroup group, int number)
    {
        Group = group;
        Number = number;
    }

    public AppGroup Group { get; }
    public int Number { get; }
    public string NoText => Number.ToString();
    public string Name { get => Group.Name; set => Group.Name = value; }
    public bool IsHidden => Group.IsHidden;
    public string CountText => Group.Entries.Count + " 项";
    public bool IsHome { get; set; }
    public string HomeButtonText => IsHome ? "★ 主界面" : "设为主界面";
    public bool CanSetHome => !IsHome && !IsHidden;
}

/// <summary>
/// 统一的分组编辑页：新增 / 删除 / 隐藏 / 设为主界面（另含重命名与排序）。
/// 在对话框内对克隆数据操作，点「完成」才写回配置，取消零副作用。
/// </summary>
public partial class GroupEditorDialog : Window
{
    private readonly BoardConfig _config;
    private readonly List<AppGroup> _normal;
    private readonly AppGroup? _favorites;
    private readonly ObservableCollection<GroupEditRow> _rows = new();
    private string _homeId;

    public GroupEditorDialog(BoardConfig config)
    {
        InitializeComponent();

        _config = config;

        // 深拷贝分组外壳（Entries 列表共享引用即可——本页不增删条目）
        _normal = config.Groups.Where(g => !g.IsFavorites).Select(CloneGroup).ToList();
        _favorites = config.Groups.FirstOrDefault(g => g.IsFavorites) is { } fav ? CloneGroup(fav) : null;
        _homeId = config.HomeGroupId;

        GroupList.ItemsSource = _rows;
        RefreshRows();
    }

    /// <summary>离屏预览/探针用：确认对话框状态。</summary>
    public string DebugState =>
        $"rows={_rows.Count} visible={_normal.Count(g => !g.IsHidden)} " +
        $"home={(string.IsNullOrWhiteSpace(_homeId) ? "none" : _homeId)} " +
        $"hidden={_normal.Count(g => g.IsHidden)} list=[{string.Join(",", _normal.Select(g => (g.IsHidden ? "-" : "") + g.Name))}]";

    /// <summary>探针模式：跳过 MessageBox 与输入对话框，直接按默认结果执行，便于离屏验证操作逻辑。</summary>
    public bool ProbeMode { get; set; }

    public IReadOnlyList<GroupEditRow> Rows => _rows;

    private void Notify(string message, string title)
    {
        if (!ProbeMode) MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static AppGroup CloneGroup(AppGroup g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        IsFavorites = g.IsFavorites,
        IsHidden = g.IsHidden,
        Entries = new List<AppEntry>(g.Entries)
    };

    private void RefreshRows()
    {
        _rows.Clear();
        for (int i = 0; i < _normal.Count; i++)
            _rows.Add(new GroupEditRow(_normal[i], i + 1) { IsHome = _normal[i].Id == _homeId });
    }

    // ---------- 设为主界面 ----------

    private void SetHome_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GroupEditRow row }) SetHome(row);
    }

    /// <summary>设为主界面。隐藏的分组不可设，返回是否成功。</summary>
    public bool SetHome(GroupEditRow row)
    {
        if (row.IsHidden)
        {
            Notify("隐藏的分组不能设为主界面，请先取消隐藏。", "分组编辑");
            return false;
        }

        _homeId = row.Group.Id;
        RefreshRows();
        return true;
    }

    // ---------- 隐藏 / 显示 ----------

    private void HideToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: GroupEditRow row } checkbox) SetHidden(row, checkbox.IsChecked == true);
    }

    /// <summary>隐藏/显示分组。主界面与最后一个可见分组不可隐藏，返回是否成功。</summary>
    public bool SetHidden(GroupEditRow row, bool hide)
    {
        if (hide && row.Group.Id == _homeId)
        {
            Notify("主界面分组不能隐藏，请先把其它分组设为主界面。", "分组编辑");
            RefreshRows();   // 还原复选框
            return false;
        }

        if (hide && _normal.Count(g => !g.IsHidden) <= 1)
        {
            Notify("至少要保留一个可见分组。", "分组编辑");
            RefreshRows();
            return false;
        }

        row.Group.IsHidden = hide;
        RefreshRows();
        return true;
    }

    // ---------- 排序 ----------

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GroupEditRow row }) Move(row, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GroupEditRow row }) Move(row, 1);
    }

    /// <summary>上移(-1)/下移(+1)，返回是否发生了移动。</summary>
    public bool Move(GroupEditRow row, int delta)
    {
        int i = _normal.IndexOf(row.Group);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _normal.Count) return false;
        (_normal[i], _normal[j]) = (_normal[j], _normal[i]);
        RefreshRows();
        return true;
    }

    // ---------- 新增 / 删除 ----------

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptDialog("新增分组", "分组名称", "新分组") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        AddGroup(dialog.Value);
    }

    /// <summary>新增分组（追加到末尾，空名回退为「新分组」）。</summary>
    public GroupEditRow AddGroup(string name)
    {
        var group = new AppGroup { Name = string.IsNullOrWhiteSpace(name) ? "新分组" : name.Trim() };
        _normal.Add(group);
        RefreshRows();
        return _rows.Last(r => r.Group == group);
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GroupEditRow row }) DeleteGroup(row);
    }

    /// <summary>删除分组；删除主界面分组时自动清除主界面设置。返回是否成功。</summary>
    public bool DeleteGroup(GroupEditRow row)
    {
        if (_normal.Count <= 1)
        {
            Notify("至少要保留一个分组。", "分组编辑");
            return false;
        }

        if (!ProbeMode)
        {
            var confirm = MessageBox.Show(this,
                $"删除分组「{row.Group.Name}」及其 {row.Group.Entries.Count} 个条目？",
                "删除分组", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return false;
        }

        _normal.Remove(row.Group);
        if (row.Group.Id == _homeId) _homeId = "";
        RefreshRows();
        return true;
    }

    // ---------- 提交 / 取消 ----------

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCommit()) return;
        CommitTo(_config);
        DialogResult = true;
    }

    /// <summary>提交前的合法性校验，探针可直接调用。</summary>
    public bool CanCommit()
    {
        if (_normal.Count == 0)
        {
            Notify("至少要保留一个分组。", "分组编辑");
            return false;
        }
        if (_normal.All(g => g.IsHidden))
        {
            Notify("至少要保留一个可见分组。", "分组编辑");
            return false;
        }
        return true;
    }

    /// <summary>把编辑结果写回配置。</summary>
    public void CommitTo(BoardConfig config)
    {
        var groups = new List<AppGroup>(_normal);
        if (_favorites is not null) groups.Add(_favorites);
        config.Groups = groups;
        config.HomeGroupId = _homeId;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
