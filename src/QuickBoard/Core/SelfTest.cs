using System.Diagnostics;
using System.Text;
using System.Windows;

namespace QuickBoard.Core;

/// <summary>
/// 无界面自检：验证导入、图标提取、配置往返、进程启动、热键注册这五条关键链路。
/// 结果写到指定文件，便于在无法截图的环境下核对。
/// </summary>
public static class SelfTest
{
    public static void Run(string reportPath)
    {
        var log = new StringBuilder();
        int pass = 0, fail = 0;

        void Check(string name, Func<string?> action)
        {
            try
            {
                var detail = action();
                if (detail is null)
                {
                    log.AppendLine($"PASS  {name}");
                    pass++;
                }
                else
                {
                    log.AppendLine($"FAIL  {name}: {detail}");
                    fail++;
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"FAIL  {name}: 异常 {ex.GetType().Name} {ex.Message}");
                fail++;
            }
        }

        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-selftest"));
        log.AppendLine($"配置目录: {store.BaseDirectory}");

        // 1. 导入真实 .mal
        BoardConfig? imported = null;
        Check("导入 E:/r81/1.mal", () =>
        {
            imported = MalImporter.ImportMal("E:/r81/1.mal");
            if (imported.Groups.Count == 0) return "没有解析出分组";

            var detail = string.Join("; ", imported.Groups.Select(g => $"{g.Name}({g.Entries.Count})"));
            log.AppendLine($"      分组: {detail}");

            var first = imported.Groups.Last().Entries.FirstOrDefault();
            if (first is null) return "分组内没有条目";
            log.AppendLine($"      样例: slot={first.Slot} name={first.Name} target={first.Target}");
            if (first.Slot == 0) return "键位没有映射上";
            return null;
        });

        // 2. 收藏栏 F1-F10 映射
        Check("导入 favorites.config", () =>
        {
            var fav = MalImporter.ImportFavorites(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MadAppLauncher", "favorites.config"));
            if (fav is null) return "未找到 favorites.config";
            var keys = fav.Entries.Select(x => x.FunctionKey).OrderBy(x => x).ToArray();
            log.AppendLine($"      条目: {fav.Entries.Count} 个，功能键: {string.Join(",", keys)}");
            if (keys.Any(k => k == 0)) return "有收藏项没有映射到 F 键";
            return null;
        });

        // 3. 图标提取（exe 走文件资源 256×256，文件夹/快捷方式走 Shell）
        Check("图标提取", () =>
        {
            IconService.Initialize(store.IconCacheDirectory);
            var samples = new[]
            {
                @"C:\Windows\System32\notepad.exe",
                @"C:\Windows\explorer.exe",
                @"C:\Windows",
                @"C:\Users\Public\Desktop"
            };

            foreach (var s in samples)
            {
                var icon = IconService.Get(s, "");
                if (icon is null) return $"取不到图标: {s}";
                log.AppendLine($"      {s} -> {icon.Width}x{icon.Height}");
            }

            // 高清回归：exe 必须从文件图标资源拿到 256×256（而非旧的 32×32）
            var exeIcon = IconService.Get(@"C:\Windows\explorer.exe", "") as System.Windows.Media.Imaging.BitmapSource;
            if (exeIcon is null) return "explorer.exe 取不到图标";
            if (exeIcon.PixelWidth < 256 || exeIcon.PixelHeight < 256)
                return $"exe 图标未达到 256×256，实际 {exeIcon.PixelWidth}x{exeIcon.PixelHeight}（高清回归失败）";

            var cached = Directory.GetFiles(store.IconCacheDirectory, "*.png");
            log.AppendLine($"      磁盘缓存: {cached.Length} 个 PNG；explorer.exe 高清 {exeIcon.PixelWidth}x{exeIcon.PixelHeight}");
            return null;
        });

        // 4. 配置保存 / 读取往返（含新增的外观与窗口设置）
        Check("配置往返", () =>
        {
            if (imported is null) return "前置导入失败";

            imported.Options.UiOpacity = 0.72;
            imported.Options.BackgroundImage = @"C:\Windows\Web\Wallpaper\test.png";
            imported.Options.BackgroundImageOpacity = 0.45;
            imported.Options.BackgroundFit = 0;
            imported.Options.BackgroundCropX = 0.15;
            imported.Options.BackgroundCropY = 0.25;
            imported.Options.BackgroundCropWidth = 0.6;
            imported.Options.BackgroundCropHeight = 0.5;
            imported.Options.WindowWidth = 1250;
            imported.Options.WindowHeight = 680;
            imported.Options.ShowFavoritesBar = false;

            var probe = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-selftest"));
            probe.Save(imported);
            var back = probe.Load();
            if (back.Groups.Count != imported.Groups.Count) return "分组数量不一致";

            var a = imported.Groups.Sum(g => g.Entries.Count);
            var b = back.Groups.Sum(g => g.Entries.Count);
            if (a != b) return $"条目数量不一致 {a} != {b}";

            if (Math.Abs(back.Options.UiOpacity - 0.72) > 0.001) return "界面不透明度没保存";
            if (Math.Abs(back.Options.BackgroundImageOpacity - 0.45) > 0.001) return "背景图不透明度没保存";
            if (back.Options.BackgroundImage != imported.Options.BackgroundImage) return "背景图路径没保存";
            if (back.Options.BackgroundFit != 0) return "背景铺放方式没保存";
            if (Math.Abs(back.Options.BackgroundCropX - 0.15) > 0.001) return "裁剪X没保存";
            if (Math.Abs(back.Options.BackgroundCropWidth - 0.6) > 0.001) return "裁剪宽度没保存";
            if (Math.Abs(back.Options.WindowWidth - 1250) > 0.001) return "窗口宽度没保存";
            if (back.Options.ShowFavoritesBar) return "收藏栏开关没保存";

            log.AppendLine($"      往返一致: {back.Groups.Count} 分组 / {b} 条目 / 外观参数齐全");
            return null;
        });

        // 5. 启动链路（用隐藏的 cmd /c exit，不打扰桌面）
        Check("进程启动", () =>
        {
            var entry = new AppEntry
            {
                Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/c exit",
                WindowState = "Minimized"
            };
            var result = Launcher.Launch(entry);
            if (!result.Ok) return result.Error;
            log.AppendLine("      Process.Start 返回成功");
            return null;
        });

        // 6. 全局热键注册 / 注销
        Check("热键注册", () =>
        {
            var window = new Window { Width = 1, Height = 1, ShowInTaskbar = false, Visibility = Visibility.Hidden };
            var service = new HotkeyService();
            var error = service.Register(window, new HotkeySetting { Ctrl = true, Alt = true, Key = "F12" });
            if (error is not null) return error;
            service.Dispose();
            log.AppendLine("      Ctrl+Alt+F12 注册并注销成功");
            return null;
        });

        // 7. 键盘三排布局：每排 10 个，行/列/槽位互相可逆，且排内键面与实体键盘一致
        Check("键盘三排布局", () =>
        {
            int total = SlotKeys.Rows * SlotKeys.Columns;
            if (total != SlotKeys.Count) return "槽位数与行列不一致";

            for (int row = 0; row < SlotKeys.Rows; row++)
            {
                var labels = Enumerable.Range(0, SlotKeys.Columns)
                    .Select(col => SlotKeys.Label(SlotKeys.SlotAt(row, col))).ToArray();
                log.AppendLine($"      第 {row + 1} 排: {string.Join(" ", labels)}");

                for (int col = 0; col < SlotKeys.Columns; col++)
                {
                    int slot = SlotKeys.SlotAt(row, col);
                    if (SlotKeys.RowOf(slot) != row || SlotKeys.ColumnOf(slot) != col)
                        return $"槽位 {slot} 反算行列不一致";
                }
            }

            if (string.Join("", Enumerable.Range(1, 10).Select(SlotKeys.Label)) != "QWERTYUIOP") return "上排不是 QWERTYUIOP";
            if (string.Join("", Enumerable.Range(11, 10).Select(SlotKeys.Label)) != "ASDFGHJKL;") return "中排不是 ASDFGHJKL;";
            if (string.Join("", Enumerable.Range(21, 10).Select(SlotKeys.Label)) != "ZXCVBNM,./") return "下排不是 ZXCVBNM,./";
            return null;
        });

        // 8. 收藏栏标记：导入后必须是 IsFavorites=true，且每页置顶显示
        Check("收藏栏标记", () =>
        {
            var fav = MalImporter.ImportFavorites(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MadAppLauncher", "favorites.config"));
            if (fav is null) return "未找到 favorites.config";
            if (!fav.IsFavorites) return "ImportFavorites 没有标记 IsFavorites";
            log.AppendLine($"      收藏栏: {fav.Entries.Count} 个条目，IsFavorites={fav.IsFavorites}");
            return null;
        });

        // 9. 外观：不透明度会被夹到合法区间，无效背景图返回 null
        Check("外观参数", () =>
        {
            if (Appearance.ClampOpacity(0) < 0.19) return "不透明度下限没夹住";
            if (Appearance.ClampOpacity(5) > 1.0) return "不透明度上限没夹住";
            if (Appearance.LoadImageBrush(@"C:\no\such\image.png") is not null) return "无效路径竟然加载成功了";
            log.AppendLine($"      不透明度夹取: 0 -> {Appearance.ClampOpacity(0)}, 5 -> {Appearance.ClampOpacity(5)}");
            return null;
        });

        // 10. 分组编辑：隐藏过滤、主界面解析、配置往返
        Check("分组编辑规则", () =>
        {
            var cfg = new BoardConfig();
            var fav = new AppGroup { Name = "收藏栏", IsFavorites = true };
            var a = new AppGroup { Name = "A" };
            var b = new AppGroup { Name = "B", IsHidden = true };
            var c = new AppGroup { Name = "C" };
            cfg.Groups.AddRange(new[] { a, b, c, fav });
            cfg.LastGroupIndex = 2;   // 指向 C

            // 隐藏的 B 与收藏栏都不应进入可选列表
            var order = GroupRules.SelectableIndices(cfg);
            if (order.Count != 2 || !order.Contains(0) || !order.Contains(2))
                return $"可选分组过滤错误: [{string.Join(",", order)}]";

            // 未设主界面：用上次的 C（索引 2）
            int initial = GroupRules.ResolveInitialIndex(cfg);
            if (initial != 2) return $"默认应回退上次分组 C，实际索引 {initial}";

            // 设 A 为主界面：优先于上次分组
            cfg.HomeGroupId = a.Id;
            if (!GroupRules.IsHomeValid(cfg)) return "A 设为主界面后校验应为有效";
            initial = GroupRules.ResolveInitialIndex(cfg);
            if (initial != 0) return $"主界面 A 应优先，实际索引 {initial}";

            // 主界面被隐藏：失效并回退到上次分组
            a.IsHidden = true;
            if (GroupRules.IsHomeValid(cfg)) return "隐藏后主界面应失效";
            initial = GroupRules.ResolveInitialIndex(cfg);
            if (initial != 2) return $"主界面失效后应回退 C，实际索引 {initial}";

            // 配置往返：IsHidden 与 HomeGroupId 都能持久化
            a.IsHidden = false;
            var probe = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-selftest"));
            probe.Save(cfg);
            var back = probe.Load();
            if (back.Groups[1].IsHidden != true) return "隐藏标记没保存";
            if (back.HomeGroupId != a.Id) return "主界面 Id 没保存";
            log.AppendLine($"      隐藏过滤/主界面回退/往返一致：可选 {order.Count} 组，主界面 {a.Id[..8]}…");
            return null;
        });

        // 11. 配置备份轮转 + 损坏自愈
        Check("配置自愈与备份", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "QuickBoard-selftest-heal");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var healStore = new ConfigStore(dir);

            var good1 = new BoardConfig();
            good1.Groups.Add(new AppGroup { Name = "First" });
            good1.Groups.Add(new AppGroup { Name = "Second" });
            good1.Groups[1].Entries.Add(new AppEntry { Name = "X", Slot = 1 });
            healStore.Save(good1);   // 第一次写入：无 .bak

            var good2 = new BoardConfig();
            good2.Groups.Add(new AppGroup { Name = "SecondVersion" });
            healStore.Save(good2);   // 第二次写入：轮转出 .bak = good1
            if (!File.Exists(healStore.BackupPath)) return "保存后没有生成 .bak 备份";
            var bak = new ConfigStore(dir).Load();   // 先确认主文件本身可读
            if (bak.Groups.Count != 1) return "第二次保存的主文件内容不对";

            // 人为写坏主文件，再 load：应从 .bak 恢复出 good1（2 组 1 条目）
            File.WriteAllText(healStore.ConfigPath, "{ this is broken json ]]]");
            var healProbe = new ConfigStore(dir);
            var recovered = healProbe.Load();
            if (healProbe.LastLoadStatus != ConfigLoadStatus.RecoveredFromBackup)
                return $"损坏后应从备份恢复，实际状态 {healProbe.LastLoadStatus}";
            if (recovered.Groups.Count != 2 || recovered.Groups.Sum(g => g.Entries.Count) != 1)
                return "恢复出来的不是备份里的数据";
            // 自愈已把好配置写回主文件：再 load 应是正常状态
            var again = new ConfigStore(dir);
            again.Load();
            if (again.LastLoadStatus != ConfigLoadStatus.Ok) return "自愈后主文件仍非正常状态";

            // 主文件与备份都坏：回退默认，不抛异常
            File.WriteAllText(healStore.ConfigPath, "broken");
            File.WriteAllText(healStore.BackupPath, "broken too");
            var dead = new ConfigStore(dir);
            var fallback = dead.Load();
            if (dead.LastLoadStatus != ConfigLoadStatus.ResetToDefault) return "双损坏应回退默认";
            if (fallback.Groups.Count == 0) return "回退的默认配置没有分组";

            var brokenKept = Directory.GetFiles(dir, "board.json.broken-*");
            if (brokenKept.Length == 0) return "损坏现场没有保留";
            log.AppendLine($"      备份轮转/损坏回退/现场保留通过；保留 {brokenKept.Length} 个 .broken 现场");
            return null;
        });

        // 12. 主题档位与图标尺寸解析
        Check("主题与图标档位", () =>
        {
            var cfg = new BoardConfig();
            cfg.Options.ThemeKind = ThemeKinds.Light;
            cfg.Options.IconScale = IconScales.Large;
            var probe = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-selftest"));
            probe.Save(cfg);
            var back = probe.Load();
            if (back.Options.ThemeKind != ThemeKinds.Light) return "主题档位没保存";
            if (back.Options.IconScale != IconScales.Large) return "图标尺寸档位没保存";

            if (Appearance.ResolveTheme(ThemeKinds.Dark) != ThemeKinds.Dark) return "深色解析错误";
            if (Appearance.ResolveTheme(ThemeKinds.Light) != ThemeKinds.Light) return "浅色解析错误";
            int follow = Appearance.ResolveTheme(ThemeKinds.System);
            if (follow != ThemeKinds.Dark && follow != ThemeKinds.Light) return "跟随系统解析错误";
            log.AppendLine($"      主题/尺寸往返一致；跟随系统解析为 {(follow == ThemeKinds.Light ? "浅色" : "深色")}");
            return null;
        });

        // 12b. 磁贴名称字号：往返 + 夹取 + 资源写入
        Check("名称字号", () =>
        {
            var cfg = new BoardConfig();
            cfg.Options.NameFontSize = 12;
            var probe = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-selftest"));
            probe.Save(cfg);
            var back = probe.Load();
            if (back.Options.NameFontSize != 12) return "名称字号没保存";
            if (NameFontSizes.Clamp(99) != NameFontSizes.Max) return "上限夹取错误";
            if (NameFontSizes.Clamp(double.NaN) != NameFontSizes.Default) return "NaN 未回默认档";
            if (NameFontSizes.Clamp(0) != NameFontSizes.Default) return "0 未回默认档";

            // 资源写入（需要 Application；--selftest 有 AppDomain 上下文则校验，否则跳过）
            var app = System.Windows.Application.Current;
            if (app is not null)
            {
                Appearance.Apply(1, ThemeKinds.Dark, IconScales.Standard, 12);
                double fs = (double)app.Resources["TileNameFontSize"];
                double fav = (double)app.Resources["FavNameFontSize"];
                double maxH = (double)app.Resources["TileNameMaxHeight"];
                if (fs != 12) return "键盘名称字号资源错误";
                if (fav != 11) return "收藏名称字号资源错误";
                if (maxH != 32) return "两行区高度资源错误";
                Appearance.Apply(1, ThemeKinds.Dark, IconScales.Standard, NameFontSizes.Default);
            }
            log.AppendLine($"      字号往返/夹取/资源写入一致（9–{NameFontSizes.Max:0}，默认 {NameFontSizes.Default:0}）");
            return null;
        });

        // 13. 诊断文本包含关键健康项
        Check("启动诊断", () =>
        {
            var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "QuickBoard-selftest"));
            store.Load();
            var text = Diagnostics.Collect(store, "热键被占用");
            foreach (var key in new[] { "配置状态", "主文件", "备份", "图标缓存", "全局热键", "异常日志", "注册失败" })
                if (!text.Contains(key)) return $"诊断文本缺少「{key}」";
            log.AppendLine("      诊断文本含配置/备份/图标/热键/日志/环境六类信息");
            return null;
        });

        // 14. 配置导出 / 导入往返
        Check("配置导出导入", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "QuickBoard-selftest-export");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var s = new ConfigStore(dir);

            var cfg = new BoardConfig();
            cfg.Groups.Add(new AppGroup { Name = "工作", Entries = new() { new AppEntry { Name = "VS", Slot = 3 } } });
            cfg.Groups.Add(new AppGroup { Name = "游戏", Entries = new() { new AppEntry { Name = "Steam", Slot = 7 } } });
            cfg.Options.ThemeKind = ThemeKinds.Light;
            cfg.Options.IconScale = IconScales.Compact;
            cfg.HomeGroupId = cfg.Groups[1].Id;

            var outPath = Path.Combine(dir, "export.json");
            if (!s.Export(cfg, outPath, out var expMsg)) return expMsg;
            if (!File.Exists(outPath)) return "导出文件没生成";

            var imported = s.ReadImport(outPath, out var impMsg);
            if (imported is null) return impMsg;
            if (imported.Groups.Count != 2 || imported.Groups.Sum(g => g.Entries.Count) != 2)
                return "导入的分组/条目数量不对";
            if (imported.Options.ThemeKind != ThemeKinds.Light || imported.Options.IconScale != IconScales.Compact)
                return "导入没带上主题/尺寸设置";
            if (imported.HomeGroupId != cfg.HomeGroupId) return "导入没带上主界面设置";

            // 坏文件应安全返回 null，不抛异常
            var badPath = Path.Combine(dir, "bad.json");
            File.WriteAllText(badPath, "not json");
            if (s.ReadImport(badPath, out _) is not null) return "坏导入文件竟然解析成功";

            log.AppendLine($"      {expMsg}；{impMsg}");
            return null;
        });

        // 15. 剪切 / 复制 / 粘贴：右键菜单的五种落位结果
        Check("剪贴板剪切粘贴", () =>
        {
            static AppEntry E(string name, int slot) =>
                new() { Name = name, Target = @"C:\Windows\notepad.exe", Slot = slot };

            static AppGroup G(string name, params AppEntry[] entries)
            {
                var group = new AppGroup { Name = name };
                group.Entries.AddRange(entries);
                return group;
            }

            // a) 跨组剪切：源少一个、目标多一个，键位跟着目标走
            var cfg = new BoardConfig();
            var work = G("工作", E("VS", 1), E("IDEA", 2));
            var game = G("游戏", E("Steam", 1));
            cfg.Groups.Add(work);
            cfg.Groups.Add(game);

            var vs = work.Entries[0];
            var moved = EntryClipboard.Paste(cfg, EntryClipboard.Create(vs, work, cut: true)!, game, 5, 0);
            if (moved != PasteOutcome.Moved) return $"跨组剪切应为 Moved，实际 {moved}";
            if (work.Entries.Contains(vs)) return "剪切后源分组里还留着条目";
            if (!game.Entries.Contains(vs) || vs.Slot != 5) return "条目没有落到目标键位 5";

            // b) 复制后连粘两次：两条各自独立的条目，源不动
            var idea = work.Entries.First(x => x.Name == "IDEA");
            var copy = EntryClipboard.Create(idea, work, cut: false)!;
            if (EntryClipboard.Paste(cfg, copy, game, 6, 0) != PasteOutcome.Copied) return "第一次复制粘贴失败";
            if (EntryClipboard.Paste(cfg, copy, game, 7, 0) != PasteOutcome.Copied) return "第二次复制粘贴失败";
            if (!work.Entries.Contains(idea)) return "复制把源条目弄丢了";

            var clones = game.Entries.Where(x => x.Name == "IDEA").ToList();
            if (clones.Count != 2) return $"应粘贴出 2 份，实际 {clones.Count}";
            if (clones[0].Id == clones[1].Id) return "两次粘贴共用了同一个 Id";
            if (clones[0].Slot == clones[1].Slot) return "两次粘贴落在了同一个键位";

            // c) 同组内剪切到已占用键位：两条互换，谁都不丢
            var cfg2 = new BoardConfig();
            var pair = G("组", E("A", 1), E("B", 2));
            cfg2.Groups.Add(pair);
            var a = pair.Entries[0];
            var b = pair.Entries[1];
            if (EntryClipboard.Paste(cfg2, EntryClipboard.Create(a, pair, cut: true)!, pair, 2, 0) != PasteOutcome.Swapped)
                return "同组占用位应当互换";
            if (a.Slot != 2 || b.Slot != 1) return $"互换后键位不对（A={a.Slot} B={b.Slot}）";
            if (pair.Entries.Count != 2) return "互换把条目弄丢了";

            // d) 复制到已占用键位：用户拒绝 → Cancelled，数据不动
            var cfg3 = new BoardConfig();
            var deny = G("组", E("A", 1), E("B", 2));
            cfg3.Groups.Add(deny);
            var refused = EntryClipboard.Paste(cfg3, EntryClipboard.Create(deny.Entries[0], deny, cut: false)!, deny, 2, 0, _ => false);
            if (refused != PasteOutcome.Cancelled) return $"拒绝替换应为 Cancelled，实际 {refused}";
            if (deny.Entries.Count != 2) return "拒绝替换后条目数变了";

            // e) 落点类型不匹配：收藏组只接 F 键
            var cfg4 = new BoardConfig();
            var plain = G("组", E("A", 1));
            var favorites = new AppGroup { Name = "收藏", IsFavorites = true };
            cfg4.Groups.Add(plain);
            cfg4.Groups.Add(favorites);
            if (EntryClipboard.Paste(cfg4, EntryClipboard.Create(plain.Entries[0], plain, cut: true)!, favorites, 5, 0)
                != PasteOutcome.NoTarget) return "字母键位不该落进收藏栏";

            log.AppendLine("      跨组剪切/连续复制/同组互换/拒绝替换/落点校验 五条路径均符合预期");
            return null;
        });

        log.AppendLine();
        log.AppendLine($"结果: {pass} 通过 / {fail} 失败");
        File.WriteAllText(reportPath, log.ToString());
    }
}
