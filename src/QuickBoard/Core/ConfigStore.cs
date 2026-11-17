using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickBoard.Core;

/// <summary>配置加载结果，供诊断页展示。</summary>
public enum ConfigLoadStatus
{
    Ok,                 // 正常读取
    RecoveredFromBackup,// 主文件损坏，已从 .bak 恢复
    ResetToDefault      // 主文件与备份都不可用，回退默认
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true
    };

    public ConfigStore(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickBoard");
        IconCacheDirectory = Path.Combine(BaseDirectory, "icons");
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
    }

    public string BaseDirectory { get; }
    public string ConfigPath => Path.Combine(BaseDirectory, "board.json");
    public string BackupPath => Path.Combine(BaseDirectory, "board.json.bak");
    public string IconCacheDirectory { get; }

    /// <summary>最近一次 Load 的结果。诊断页读取。</summary>
    public ConfigLoadStatus LastLoadStatus { get; private set; } = ConfigLoadStatus.Ok;

    /// <summary>最近一次 Load/Save 的详细说明（含异常信息），诊断页读取。</summary>
    public string LastMessage { get; private set; } = "";

    public BoardConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            LastLoadStatus = ConfigLoadStatus.Ok;
            LastMessage = "配置文件不存在，已使用默认配置。";
            return CreateDefault();
        }

        // 1) 主文件正常读取
        if (TryRead(ConfigPath, out var cfg, out var mainError))
        {
            LastLoadStatus = ConfigLoadStatus.Ok;
            LastMessage = "配置读取正常。";
            return cfg!;
        }

        // 2) 主文件损坏：保留现场，再尝试从 .bak 恢复
        PreserveBroken(ConfigPath);
        if (File.Exists(BackupPath) && TryRead(BackupPath, out var backup, out _))
        {
            LastLoadStatus = ConfigLoadStatus.RecoveredFromBackup;
            LastMessage = $"主配置损坏（{Short(mainError)}），已自动从备份恢复。";
            Save(backup!);   // 立刻把好配置写回主文件，避免下次又走恢复路径
            return backup!;
        }

        // 3) 主文件与备份都不可用：回退默认，不覆盖损坏文件（现场已保留）
        LastLoadStatus = ConfigLoadStatus.ResetToDefault;
        LastMessage = $"主配置损坏（{Short(mainError)}）且无可用备份，已回退默认配置。";
        return CreateDefault();
    }

    public void Save(BoardConfig config)
    {
        try
        {
            var tmp = ConfigPath + ".tmp";

            // 轮转备份：写入新文件前，把当前这份「尚完好」的配置留一份 .bak。
            // 仅在本次能成功解析时备份，避免把损坏内容固化进备份链。
            if (File.Exists(ConfigPath) && TryRead(ConfigPath, out _, out _))
                File.Copy(ConfigPath, BackupPath, true);

            File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
            File.Move(tmp, ConfigPath, true);
            LastMessage = "配置保存正常。";
        }
        catch (Exception ex)
        {
            LastMessage = "保存配置失败: " + ex.Message;
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex}");
        }
    }

    /// <summary>把配置导出到用户指定的路径（导出的是当前内存中的完整配置）。</summary>
    public bool Export(BoardConfig config, string path, out string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
            message = $"已导出到 {path}";
            return true;
        }
        catch (Exception ex)
        {
            message = "导出失败: " + ex.Message;
            return false;
        }
    }

    /// <summary>从指定路径读取配置做预览校验；不改动当前配置，由调用方决定是否应用。</summary>
    public BoardConfig? ReadImport(string path, out string message)
    {
        try
        {
            if (!File.Exists(path)) { message = "文件不存在"; return null; }
            if (!TryParse(File.ReadAllText(path), out var cfg, out var error))
            {
                message = "导入文件解析失败: " + Short(error);
                return null;
            }

            message = $"可导入：{cfg!.Groups.Count} 个分组 / {cfg.Groups.Sum(g => g.Entries.Count)} 个条目";
            return cfg;
        }
        catch (Exception ex)
        {
            message = "导入文件解析失败: " + ex.Message;
            return null;
        }
    }

    /// <summary>导入并落盘，覆盖当前配置；Save 会自动轮转出上一版备份。</summary>
    public void ApplyImport(BoardConfig config)
    {
        Save(config);
    }

    private bool TryRead(string path, out BoardConfig? cfg, out string? error)
    {
        try
        {
            return TryParse(File.ReadAllText(path), out cfg, out error);
        }
        catch (Exception ex)
        {
            cfg = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>解析 JSON 并补齐可能缺失的集合/子对象，再做旧配置迁移。</summary>
    private static bool TryParse(string json, out BoardConfig? cfg, out string? error)
    {
        cfg = null;
        error = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<BoardConfig>(json, JsonOptions);
            if (parsed is null) { error = "反序列化结果为空"; return false; }

            parsed.Groups ??= new List<AppGroup>();
            parsed.Hotkey ??= new HotkeySetting();
            parsed.Options ??= new BoardOptions();
            foreach (var g in parsed.Groups) g.Entries ??= new List<AppEntry>();

            Normalize(parsed);
            cfg = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void PreserveBroken(string path)
    {
        try
        {
            var backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(path, backup, true);
        }
        catch { /* 保留现场失败不影响恢复流程 */ }
    }

    private static string Short(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "解析异常";
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    private static BoardConfig CreateDefault()
    {
        var cfg = new BoardConfig();
        cfg.Groups.Add(new AppGroup { Name = "常用" });
        return cfg;
    }

    /// <summary>
    /// 兼容旧配置：老版本把 favorites.config 当成普通分组存进来，
    /// 这里把「条目全是 F 键」的那个分组标成收藏栏，迁移到置顶显示。
    /// </summary>
    private static void Normalize(BoardConfig cfg)
    {
        if (cfg.Groups.Count == 0) return;
        if (cfg.Groups.Any(g => g.IsFavorites)) return;

        var candidate = cfg.Groups.FirstOrDefault(g =>
            g.Entries.Count > 0 && g.Entries.All(e => e.FunctionKey > 0));

        if (candidate is not null) candidate.IsFavorites = true;
    }
}
