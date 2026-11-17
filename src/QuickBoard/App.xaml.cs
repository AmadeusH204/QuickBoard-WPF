using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using QuickBoard.Core;

namespace QuickBoard;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;
    private System.Windows.Forms.NotifyIcon? _tray;
    private ConfigStore? _store;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 离屏探针 / 渲染入口（--selftest、--layoutdump、--boardshot 等）：处理完直接退出
        if (TryRunProbe(e.Args))
        {
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, "QuickBoard.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting("QuickBoard.ShowEvent").Set(); } catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "QuickBoard.ShowEvent");
        var listener = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                Dispatcher.BeginInvoke(new Action(() => _window?.ShowAndFocus()));
            }
        })
        { IsBackground = true };
        listener.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickBoard");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {args.Exception}\r\n\r\n");
            }
            catch
            {
                // 写日志失败也要保证界面还能用
            }

            MessageBox.Show("出错了：" + args.Exception.Message, "QuickBoard");
            args.Handled = true;
        };

        _store = new ConfigStore();
        var config = _store.Load();

        // 配置损坏并自愈/回退时，明确告知用户（设置页「诊断」区也会保留详情）
        if (_store.LastLoadStatus != ConfigLoadStatus.Ok)
        {
            var title = _store.LastLoadStatus == ConfigLoadStatus.RecoveredFromBackup
                ? "配置已从备份恢复" : "配置已回退默认";
            MessageBox.Show(_store.LastMessage, title, MessageBoxButton.OK,
                _store.LastLoadStatus == ConfigLoadStatus.RecoveredFromBackup
                    ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        var importArg = e.Args.FirstOrDefault(a => a.EndsWith(".mal", StringComparison.OrdinalIgnoreCase));
        if (importArg is not null && File.Exists(importArg))
        {
            try
            {
                config.Groups = MalImporter.ImportMal(importArg).Groups;
                _store.Save(config);
            }
            catch
            {
                // 导入失败时保持原配置
            }
        }

        _window = new MainWindow(_store, config);
        SetupTray(config);

        if (!config.Options.StartHidden) _window.ShowAndFocus();

        if (e.Args.Contains("--opensettings"))
        {
            Dispatcher.BeginInvoke(new Action(() => _window.OpenSettings()),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// 离屏探针分发：参数形如 <c>--name &lt;path&gt; [额外参数…]</c>。
    /// 返回 true 表示这次启动是探针调用，调用方应直接退出。
    /// 所有探针都是离屏渲染/只读诊断，不写回用户配置（MainWindow 以 SuppressPersist 创建）。
    /// </summary>
    private static bool TryRunProbe(string[] args)
    {
        int IndexOf(string name) => Array.FindIndex(args, a => a == name);

        /// <summary>取 <c>--name</c> 后面跟的值；缺省时用临时目录下的默认名。</summary>
        string ArgOr(string name, string fallback)
        {
            int i = IndexOf(name);
            return i >= 0 && i + 1 < args.Length
                ? args[i + 1]
                : Path.Combine(Path.GetTempPath(), fallback);
        }

        if (IndexOf("--selftest") >= 0)
        {
            Core.SelfTest.Run(ArgOr("--selftest", "quickboard-selftest.txt"));
            return true;
        }

        if (IndexOf("--launchprobe") is var launch && launch >= 0)
        {
            // 可选第二参：要启动的目标（默认 notepad.exe）
            string? target = launch + 2 < args.Length && !args[launch + 2].StartsWith("--") ? args[launch + 2] : null;
            Core.LaunchProbe.Run(ArgOr("--launchprobe", "quickboard-launch.txt"), target);
            return true;
        }

        if (IndexOf("--layoutdump") is var layout && layout >= 0)
        {
            double width = layout + 3 < args.Length && double.TryParse(args[layout + 2], out var w) ? w : 1180;
            double height = layout + 3 < args.Length && double.TryParse(args[layout + 3], out var h) ? h : 660;
            var store = new ConfigStore();
            Core.LayoutProbe.Run(store, store.Load(), ArgOr("--layoutdump", "quickboard-layout.txt"), width, height);
            return true;
        }

        if (IndexOf("--boardshot") is var shot && shot >= 0)
        {
            var store = new ConfigStore();
            var config = store.Load();
            // 可选第二参：临时覆盖名称字号（只改内存配置，不写盘）
            if (shot + 2 < args.Length && double.TryParse(args[shot + 2], out var fontSize))
                config.Options.NameFontSize = NameFontSizes.Clamp(fontSize);
            Core.BoardShot.Run(store, config, ArgOr("--boardshot", "quickboard-boardshot.png"));
            return true;
        }

        if (IndexOf("--memprobe") is var mem && mem >= 0)
        {
            var store = new ConfigStore();
            string mode = mem + 2 < args.Length ? args[mem + 2] : "";
            Core.MemProbe.Run(store, store.Load(), ArgOr("--memprobe", "quickboard-mem.txt"), mode);
            return true;
        }

        if (IndexOf("--nameprobe") >= 0)
        {
            Core.NameProbe.Run(ArgOr("--nameprobe", "quickboard-nameprobe.png"));
            return true;
        }

        if (IndexOf("--uipreview") >= 0)
        {
            var dir = ArgOr("--uipreview", "quickboard-preview");
            var store = new ConfigStore();
            Core.DialogPreview.Run(store.Load(), store, dir, Path.Combine(dir, "report.txt"));
            return true;
        }

        if (IndexOf("--menuprobe") >= 0)
        {
            var dir = ArgOr("--menuprobe", "quickboard-menuprobe");
            var store = new ConfigStore();
            Core.MenuProbe.Run(store, store.Load(), dir, Path.Combine(dir, "report.txt"));
            return true;
        }

        if (IndexOf("--themeprobe") >= 0)
        {
            var dir = ArgOr("--themeprobe", "quickboard-themeprobe");
            var store = new ConfigStore();
            Core.ThemeProbe.Run(store, store.Load(), dir, Path.Combine(dir, "report.txt"));
            return true;
        }

        if (IndexOf("--comboprobe") >= 0)
        {
            var dir = ArgOr("--comboprobe", "quickboard-comboprobe");
            Core.ComboProbe.Run(Path.Combine(dir, "report.txt"), dir);
            return true;
        }

        if (IndexOf("--iconprobe") >= 0)
        {
            var store = new ConfigStore();
            Core.IconProbe.Run(store.Load(), ArgOr("--iconprobe", "quickboard-iconprobe.txt"));
            return true;
        }

        if (IndexOf("--groupprobe") >= 0)
        {
            Core.GroupProbe.Run(new BoardConfig(), ArgOr("--groupprobe", "quickboard-groupprobe.txt"));
            return true;
        }

        return false;
    }

    private void SetupTray(BoardConfig config)
    {
        if (!config.Options.ShowTrayIcon) return;

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "QuickBoard",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => _window?.ToggleVisible());
        menu.Items.Add("设置…", null, (_, _) => _window?.OpenSettings());
        menu.Items.Add("重新载入配置", null, (_, _) => _window?.ReloadFromDisk());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("打开配置目录", null, (_, _) =>
        {
            if (_store is not null) System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_store.BaseDirectory) { UseShellExecute = true });
        });
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _window?.ToggleVisible();
    }

    private void ExitApp()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
