using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace QuickBoard.Core;

/// <summary>
/// 只读解析 MadAppLauncher 的 *.mal 与 favorites.config，转换成 QuickBoard 的模型。
/// 不修改任何原文件。
/// </summary>
public static class MalImporter
{
    private static string Val(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value?.Trim() ?? "";

    private static string CleanPath(string raw)
    {
        var s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s.Trim();
    }

    private static int SlotFromButtonId(string buttonId)
    {
        if (string.IsNullOrWhiteSpace(buttonId)) return 0;
        var id = buttonId.Trim();
        if (!id.StartsWith("btnMA", StringComparison.OrdinalIgnoreCase)) return 0;
        var suffix = id[5..].ToUpperInvariant();
        return suffix switch
        {
            "Q" => 1, "W" => 2, "E" => 3, "R" => 4, "T" => 5,
            "Y" => 6, "U" => 7, "I" => 8, "O" => 9, "P" => 10,
            "A" => 11, "S" => 12, "D" => 13, "F" => 14, "G" => 15,
            "H" => 16, "J" => 17, "K" => 18, "L" => 19, "SEMICOLON" => 20,
            "Z" => 21, "X" => 22, "C" => 23, "V" => 24, "B" => 25,
            "N" => 26, "M" => 27, "COMMA" => 28, "PERIOD" => 29, "SLASH" => 30,
            _ => 0
        };
    }

    /// <summary>收藏栏文件可能在 .mal 同目录，也可能在 MadAppLauncher 的本地数据目录。</summary>
    private static string? FindFavorites(string malPath)
    {
        var beside = Path.Combine(Path.GetDirectoryName(malPath) ?? "", "favorites.config");
        if (File.Exists(beside)) return beside;

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MadAppLauncher", "favorites.config");
        if (File.Exists(local)) return local;

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MadAppLauncher", "favorites.config");
        return File.Exists(roaming) ? roaming : null;
    }

    public static BoardConfig ImportMal(string malPath)
    {
        var doc = XDocument.Load(malPath);
        var root = doc.Root ?? throw new InvalidDataException("不是有效的 .mal 文件");
        var config = new BoardConfig();

        if (FindFavorites(malPath) is { } favoritesPath)
        {
            var fav = ImportFavorites(favoritesPath);
            if (fav is not null) config.Groups.Add(fav);
        }

        var tabs = root.Elements().Where(e => e.Name.LocalName == "MATab").ToList();
        int groupIndex = 0;

        foreach (var tab in tabs)
        {
            groupIndex++;
            var name = Val(tab, "tabName");
            var group = new AppGroup
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"分组 {groupIndex}" : name
            };

            int order = 0;
            foreach (var btn in tab.Elements().Where(e => e.Name.LocalName == "MAButton"))
            {
                order++;
                var target = CleanPath(Val(btn, "fileName"));
                if (string.IsNullOrWhiteSpace(target)) continue;

                var slot = SlotFromButtonId(Val(btn, "buttonID"));
                if (slot == 0 && order <= SlotKeys.Count) slot = order;

                var text = Val(btn, "buttonText");
                var entry = new AppEntry
                {
                    Name = string.IsNullOrWhiteSpace(text)
                        ? Path.GetFileNameWithoutExtension(CleanPath(target))
                        : text,
                    Target = target,
                    Arguments = Val(btn, "arguments"),
                    WorkingDirectory = CleanPath(Val(btn, "workingDirectory")),
                    IconPath = CleanPath(Val(btn, "iconFile")),
                    Slot = slot,
                    RunAsAdmin = string.Equals(Val(btn, "runAsAdmin"), "true", StringComparison.OrdinalIgnoreCase),
                    WindowState = Val(btn, "windowStyle") switch
                    {
                        "1" => "Minimized",
                        "2" => "Maximized",
                        _ => "Normal"
                    },
                    WindowMoveDelay = int.TryParse(Val(btn, "windowMoveDelay"), out var d) && d > 0 ? d : 1000
                };

                group.Entries.Add(entry);
            }

            if (group.Entries.Count > 0) config.Groups.Add(group);
        }

        if (config.Groups.Count == 0) config.Groups.Add(new AppGroup { Name = "常用" });
        return config;
    }

    /// <summary>解析 favorites.config，F1–F10 映射到 FunctionKey。</summary>
    public static AppGroup? ImportFavorites(string favoritesPath)
    {
        if (!File.Exists(favoritesPath)) return null;
        var doc = XDocument.Load(favoritesPath);
        var root = doc.Root;
        if (root is null) return null;

        var group = new AppGroup { Name = "收藏栏", IsFavorites = true };
        foreach (var node in root.Elements().Where(e => e.Name.LocalName == "FavoritesButton"))
        {
            var target = CleanPath(Val(node, "fileName"));
            if (string.IsNullOrWhiteSpace(target)) continue;

            var id = Val(node, "buttonID");
            var fkey = 0;
            if (id.StartsWith("FavoritesButtonF", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id["FavoritesButtonF".Length..], out var n) && n >= 1 && n <= 12)
            {
                fkey = n;
            }

            var text = Val(node, "buttonText");
            group.Entries.Add(new AppEntry
            {
                Name = string.IsNullOrWhiteSpace(text) ? Path.GetFileNameWithoutExtension(CleanPath(target)) : text,
                Target = target,
                Arguments = Val(node, "arguments"),
                WorkingDirectory = CleanPath(Val(node, "workingDirectory")),
                IconPath = CleanPath(Val(node, "iconFile")),
                RunAsAdmin = string.Equals(Val(node, "runAsAdmin"), "true", StringComparison.OrdinalIgnoreCase),
                FunctionKey = fkey
            });
        }

        return group.Entries.Count > 0 ? group : null;
    }
}
