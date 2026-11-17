using System.Diagnostics;
using System.IO;

namespace QuickBoard.Core;

public static class ShortcutResolver
{
    /// <summary>.lnk 解析成真实目标；其他路径原样返回。</summary>
    public static string ResolveTarget(string path)
    {
        try
        {
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return path;
            dynamic? shell = Activator.CreateInstance(type);
            if (shell is null) return path;
            dynamic lnk = shell.CreateShortcut(path);
            string? target = lnk.TargetPath as string;
            return string.IsNullOrWhiteSpace(target) ? path : target;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>打开条目所在的文件夹并选中它。</summary>
    public static void OpenContainingFolder(string path)
    {
        try
        {
            var target = ResolveTarget(path);
            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
                return;
            }

            var dir = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开位置失败: {ex.Message}");
        }
    }
}
