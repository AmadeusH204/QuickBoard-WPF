using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace QuickBoard.Core;

/// <summary>
/// 分组编辑探针：离屏实例化 GroupEditorDialog，程序化执行新增/删除/隐藏/设为主界面/排序，
/// 核对每一步状态与最终写回的配置。沙箱无法注入鼠标点击，故用它验证交互逻辑。
/// </summary>
public static class GroupProbe
{
    public static void Run(BoardConfig config, string reportPath)
    {
        var log = new StringBuilder();
        int pass = 0, fail = 0;

        void Check(string name, Func<string?> action)
        {
            try
            {
                var detail = action();
                if (detail is null) { log.AppendLine($"PASS  {name}"); pass++; }
                else { log.AppendLine($"FAIL  {name}: {detail}"); fail++; }
            }
            catch (Exception ex)
            {
                log.AppendLine($"FAIL  {name}: 异常 {ex.GetType().Name} {ex.Message}");
                fail++;
            }
        }

        // 用一份独立配置，避免污染用户数据
        var cfg = new BoardConfig();
        var g1 = new AppGroup { Name = "常用" };
        var g2 = new AppGroup { Name = "工作" };
        var g3 = new AppGroup { Name = "游戏" };
        var fav = new AppGroup { Name = "收藏栏", IsFavorites = true };
        cfg.Groups.AddRange(new[] { g1, g2, g3, fav });
        cfg.LastGroupIndex = 0;

        var dialog = new Views.GroupEditorDialog(cfg) { ProbeMode = true };

        Check("初始渲染 3 个普通分组（收藏栏不进编辑列表）", () =>
            dialog.Rows.Count == 3 ? null : $"实际 {dialog.Rows.Count} 行");

        Check("新增分组", () =>
        {
            var row = dialog.AddGroup("新分组");
            return dialog.Rows.Count == 4 && row.Name == "新分组" ? null : "新增后行数/名称不对";
        });

        Check("重命名分组", () =>
        {
            dialog.Rows[3].Name = "学习";
            return dialog.Rows[3].Name == "学习" ? null : "重命名没生效";
        });

        Check("设为主界面", () =>
        {
            bool ok = dialog.SetHome(dialog.Rows[1]);
            return ok && dialog.Rows[1].IsHome && !dialog.Rows[1].CanSetHome
                ? null : "主界面标记未生效";
        });

        Check("主界面分组不可隐藏", () =>
            !dialog.SetHidden(dialog.Rows[1], true) && !dialog.Rows[1].Group.IsHidden
                ? null : "主界面仍被隐藏");

        Check("隐藏普通分组", () =>
            dialog.SetHidden(dialog.Rows[2], true) && dialog.Rows[2].Group.IsHidden
                ? null : "隐藏未生效");

        Check("隐藏分组不可设为主界面", () =>
            !dialog.SetHome(dialog.Rows[2]) ? null : "隐藏分组竟被设为主界面");

        Check("上移/下移排序", () =>
        {
            string before = dialog.Rows[0].Name;
            bool moved = dialog.Move(dialog.Rows[1], -1);
            return moved && dialog.Rows[0].Name == "工作" && dialog.Rows[1].Name == before
                ? null : $"排序后顺序不对: {dialog.DebugState}";
        });

        Check("删除分组（非空组）", () =>
            dialog.DeleteGroup(dialog.Rows[3]) && dialog.Rows.Count == 3
                ? null : "删除未生效");

        Check("删除主界面分组会清除主界面 Id", () =>
        {
            var homeRow = dialog.Rows.First(r => r.IsHome);
            bool ok = dialog.DeleteGroup(homeRow);
            var groups = new BoardConfig();
            dialog.CommitTo(groups);
            return ok && string.IsNullOrWhiteSpace(groups.HomeGroupId)
                ? null : $"主界面未清除: {groups.HomeGroupId}";
        });

        Check("提交后配置结构正确（收藏栏保留在末尾）", () =>
        {
            var cfg2 = new BoardConfig();
            dialog.CommitTo(cfg2);
            return cfg2.Groups.Count == dialog.Rows.Count + 1
                   && cfg2.Groups.Last().IsFavorites
                   && cfg2.Groups.Count(g => g.IsHidden) == 1
                ? null : $"提交结构不对: groups={cfg2.Groups.Count} hidden={cfg2.Groups.Count(g => g.IsHidden)}";
        });

        Check("取消不写回原配置", () =>
        {
            // dialog 从未对原始 cfg 调 CommitTo，原 cfg 应不变（仍 4 组、无隐藏、无主界面）
            return cfg.Groups.Count == 4 && !cfg.Groups.Any(g => g.IsHidden)
                   && string.IsNullOrWhiteSpace(cfg.HomeGroupId)
                ? null : "取消前原配置被意外修改";
        });

        // 主窗口：设主界面 + 隐藏分组后，分组栏只显示可见组且主界面带 ★
        Check("主窗口分组栏：隐藏过滤 + ★ 主界面标记", () =>
        {
            var live = new BoardConfig();
            var gA = new AppGroup { Name = "常用" };
            var gB = new AppGroup { Name = "工作", IsHidden = true };
            var gC = new AppGroup { Name = "游戏" };
            live.Groups.AddRange(new[] { gA, gB, gC, new AppGroup { Name = "收藏栏", IsFavorites = true } });
            live.HomeGroupId = gC.Id;
            live.LastGroupIndex = 0;

            var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-groupprobe"));
            var window = new MainWindow(store, live)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -6000, Top = -6000, ShowInTaskbar = false, SuppressPersist = true
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

                var pills = window.GroupBar.Items.Cast<GroupPill>().ToList();
                if (pills.Count != 2) return $"分组栏应 2 项（隐藏的不显示），实际 {pills.Count}";
                if (pills.Any(p => p.Name.Contains("工作"))) return "隐藏分组仍显示在分组栏";
                var home = pills.FirstOrDefault(p => p.Name.Contains("游戏"));
                if (home is null || !home.Name.StartsWith("★")) return $"主界面分组缺 ★ 标记: {string.Join(" | ", pills.Select(p => p.Name))}";
                if (!home.IsSelected) return "启动应选中主界面分组";
                log.AppendLine($"      分组栏: {string.Join(" | ", pills.Select(p => p.Name))}");
                return null;
            }
            finally
            {
                window.Hide();
                window.Close();
            }
        });

        log.AppendLine();
        log.AppendLine($"结果: {pass} 通过 / {fail} 失败");
        log.AppendLine("最终状态: " + dialog.DebugState);
        System.IO.File.WriteAllText(reportPath, log.ToString(), Encoding.UTF8);
    }
}
