namespace QuickBoard.Core;

/// <summary>剪贴板里的一条记录。cut=true 时持有原条目引用，粘贴时才真正从源分组移除。</summary>
public sealed record ClipPiece(AppEntry Entry, string SourceGroupId, bool IsCut, string DisplayName);

/// <summary>粘贴结果，用于给状态栏/提示框选文案。</summary>
public enum PasteOutcome
{
    Moved,        // 剪切后粘贴：条目从源位置搬到目标位置
    Copied,       // 复制后粘贴：目标位置多出新条目
    Swapped,      // 同组内剪切粘贴到已占用键位：两条互换位置
    Replaced,     // 复制/跨组粘贴到已占用键位：旧条目被替换
    AlreadyHere,  // 粘贴到条目自身所在位置
    Cancelled,    // 用户拒绝替换
    NoTarget      // 没有可放的位置
}

/// <summary>
/// 条目剪贴板：对齐原版 MadAppLauncher 的 Cut / Copy / Paste / Delete 语义。
/// 逻辑与界面解耦（不引用 WPF），SelfTest 可直接断言。
/// 与原生实现的一处刻意差异：<b>剪切不立即从源分组删除</b>，粘贴时才移动，
/// 避免「剪切后忘了粘贴」把条目弄丢；观感差异只在剪切与粘贴之间的那一小段时间。
/// </summary>
public static class EntryClipboard
{
    /// <summary>创建剪贴板记录；空条目（没有目标）不允许进入剪贴板。</summary>
    public static ClipPiece? Create(AppEntry entry, AppGroup? owner, bool cut)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Target)) return null;
        var piece = cut ? entry : Clone(entry);
        return new ClipPiece(piece, owner?.Id ?? "", cut, DisplayName(entry));
    }

    /// <summary>复制一份条目（新 Id、不绑定键位），供「复制→多次粘贴」各得一条独立记录。</summary>
    public static AppEntry Clone(AppEntry source) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = source.Name,
        Target = source.Target,
        Arguments = source.Arguments,
        WorkingDirectory = source.WorkingDirectory,
        IconPath = source.IconPath,
        RunAsAdmin = source.RunAsAdmin,
        WindowState = source.WindowState,
        WindowPlacement = source.WindowPlacement,
        WindowMoveDelay = source.WindowMoveDelay
    };

    public static string DisplayName(AppEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Name)) return entry.Name;
        var file = Path.GetFileNameWithoutExtension(entry.Target);
        return string.IsNullOrWhiteSpace(file) ? entry.Target : file;
    }

    /// <summary>键位徽标文字：收藏位 F1–F12，字母键位 Q../，无键位返回空。</summary>
    public static string LabelOf(int slot, int functionKey) =>
        functionKey > 0 ? "F" + functionKey : SlotKeys.Label(slot);

    /// <summary>
    /// 把剪贴板内容放到 <paramref name="target"/> 的指定键位。
    /// 目标位置已有条目时：同组内剪切 → 两条互换；其余情况先问 <paramref name="confirmReplace"/>，允许才替换。
    /// </summary>
    public static PasteOutcome Paste(
        BoardConfig config, ClipPiece clip, AppGroup target, int slot, int functionKey,
        Func<AppEntry, bool>? confirmReplace = null)
    {
        if (config is null || clip is null || target is null) return PasteOutcome.NoTarget;

        bool toFavorites = functionKey > 0;
        if (toFavorites && !target.IsFavorites) return PasteOutcome.NoTarget;   // F 键只能落在收藏栏
        if (!toFavorites && target.IsFavorites) return PasteOutcome.NoTarget;   // 收藏栏只接 F 键
        if (!toFavorites && (slot <= 0 || slot > SlotKeys.Count)) return PasteOutcome.NoTarget;
        if (toFavorites && (functionKey <= 0 || functionKey > 12)) return PasteOutcome.NoTarget;

        var entry = clip.IsCut ? clip.Entry : Clone(clip.Entry);
        var occupant = FindOccupant(target, slot, functionKey);

        if (ReferenceEquals(occupant, entry)) return PasteOutcome.AlreadyHere;

        if (occupant is not null)
        {
            bool sameGroupCut = clip.IsCut && target.Entries.Contains(entry);
            bool hasHome = entry.Slot > 0 || entry.FunctionKey > 0;

            // 同组内剪切：直接互换，不丢数据也不用问（先让条目占目标位，再把原占位者挪回旧位）
            if (sameGroupCut && hasHome)
            {
                int oldSlot = entry.Slot, oldKey = entry.FunctionKey;
                Place(entry, slot, functionKey);
                Place(occupant, oldSlot, oldKey);
                return PasteOutcome.Swapped;
            }

            if (confirmReplace is not null && !confirmReplace(occupant)) return PasteOutcome.Cancelled;
            target.Entries.Remove(occupant);
        }

        if (clip.IsCut)
        {
            var source = config.Groups.FirstOrDefault(g => g.Id == clip.SourceGroupId);
            source?.Entries.Remove(entry);
        }

        if (!target.Entries.Contains(entry)) target.Entries.Add(entry);
        Place(entry, slot, functionKey);
        return clip.IsCut ? PasteOutcome.Moved : PasteOutcome.Copied;
    }

    public static AppEntry? FindOccupant(AppGroup group, int slot, int functionKey) =>
        functionKey > 0
            ? group.Entries.FirstOrDefault(x => x.FunctionKey == functionKey)
            : group.Entries.FirstOrDefault(x => x.FunctionKey == 0 && x.Slot == slot);

    /// <summary>把条目钉在某个键位上（F 键与字母键位互斥）。</summary>
    public static void Place(AppEntry entry, int slot, int functionKey)
    {
        if (functionKey > 0)
        {
            entry.FunctionKey = functionKey;
            entry.Slot = 0;
        }
        else
        {
            entry.Slot = slot;
            entry.FunctionKey = 0;
        }
    }

    public static int NextFreeSlot(AppGroup group)
    {
        var used = group.Entries.Where(x => x.Slot > 0).Select(x => x.Slot).ToHashSet();
        for (int i = 1; i <= SlotKeys.Count; i++)
        {
            if (!used.Contains(i)) return i;
        }

        return 0;
    }

    public static int NextFreeFunctionKey(AppGroup favorites)
    {
        var used = favorites.Entries.Where(x => x.FunctionKey > 0).Select(x => x.FunctionKey).ToHashSet();
        for (int i = 1; i <= 12; i++)
        {
            if (!used.Contains(i)) return i;
        }

        return 0;
    }
}
