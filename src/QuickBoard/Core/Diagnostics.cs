using System.IO;
using System.Linq;
using System.Text;

namespace QuickBoard.Core;

/// <summary>
/// 启动健康诊断：把配置加载状态、备份、图标缓存、热键注册等运行时信息汇总成一段可读文本，
/// 供设置页「诊断」区展示，也便于用户复制反馈问题。
/// </summary>
public static class Diagnostics
{
    public static string Collect(ConfigStore store, string? hotkeyError)
    {
        var log = new StringBuilder();

        // 配置加载状态
        var status = store.LastLoadStatus switch
        {
            ConfigLoadStatus.RecoveredFromBackup => "已从备份恢复",
            ConfigLoadStatus.ResetToDefault => "损坏，已回退默认",
            _ => "正常"
        };
        log.AppendLine($"配置状态：{status}");
        log.AppendLine($"  主文件：{(File.Exists(store.ConfigPath) ? "存在" : "缺失")}  {store.ConfigPath}");
        log.AppendLine($"  备份：  {(File.Exists(store.BackupPath) ? "存在" : "无")}");
        log.AppendLine($"  说明：  {store.LastMessage}");

        // 图标缓存
        int iconCount = 0;
        try { iconCount = Directory.GetFiles(store.IconCacheDirectory, "*.png").Length; } catch { }
        log.AppendLine($"图标缓存：{iconCount} 个 PNG");

        // 热键
        log.AppendLine($"全局热键：{(string.IsNullOrWhiteSpace(hotkeyError) ? "注册成功" : "注册失败 - " + hotkeyError)}");

        // 错误日志
        var errorLog = Path.Combine(store.BaseDirectory, "error.log");
        int errors = 0;
        if (File.Exists(errorLog))
        {
            try { errors = File.ReadLines(errorLog).Count(l => l.StartsWith("[")); } catch { }
        }
        log.AppendLine($"异常日志：{errors} 条" + (errors > 0 ? $"  {errorLog}" : ""));

        // 环境
        log.AppendLine($"运行时：  .NET 8 / WPF · PerMonitorV2 DPI");
        return log.ToString().TrimEnd();
    }
}
