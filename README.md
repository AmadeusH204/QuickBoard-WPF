# QuickBoard

Windows 键盘启动器：三排键位铺满一屏，按一下 `Q` 启动 QQ。

QuickBoard 是 [MadAppLauncher](https://sourceforge.net/projects/madapplauncher/)（2012 年 VB.NET / WinForms 启动器）的 **.NET 8 + WPF 重写版**。它保留了原版"键位即位置"的使用手感，并补上了原版做不到的部分：任意分组数量、高清图标、自定义背景、主题、配置自愈、导入导出。

## 功能

**键盘**

- 30 个键位，按物理键盘行对齐：`Q W E R T Y U I O P` / `A S D F G H J K L ;` / `Z X C V B N M , . /`
- 分组数量不限；`1`–`0` 切前十个分组，滚轮切全部
- 收藏栏 `F1`–`F12` 固定占位，跨分组常驻
- 全局热键（默认 `Ctrl+Alt+Space`），录制式设置，冲突时保留原热键并提示
- 拖放文件或快捷方式到窗口即添加，自动占用空位
- 单实例、托盘常驻、开机自启、`Esc` 隐藏

**外观**

- 图标按 256×256 提取（`PrivateExtractIcons` 取文件图标资源的最高分辨率帧），三档显示尺寸，窗口放大不模糊
- 自定义背景图 + 界面透明度 20%–100%，带裁剪预览
- 深色 / 浅色 / 跟随系统三种主题
- 磁贴名称固定深黑 + 白色描边（壁纸深色区域也清晰），字号 9–13 可调，超长名称末行省略
- 窗口等比缩放（`Viewbox`）+ 八向边缘拖动调整大小

**数据安全**

- 配置存 `%APPDATA%\QuickBoard\board.json`；损坏时自动回退 `.bak` 并在界面提示，坏文件现场保留
- 一键备份 / 导出 / 导入
- 启动诊断页：条目数、图标缓存、加载耗时
- 只读导入原版 `.mal` 配置（不修改原文件）

**右键菜单**

`启动` · `打开所在位置` · `编辑…` ─ `剪切` / `复制` / `粘贴` / `删除` ─ `加入 / 移出收藏栏` · `设置…`

同组内剪切到已占用键位是两条**互换**；复制或跨组粘贴到已占用键位会先确认；剪切延迟到粘贴时才删除源条目，避免中途丢失。

## 运行

需要 Windows 10 / 11 与 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
dotnet run --project src/QuickBoard/QuickBoard.csproj
```

## 构建

```powershell
dotnet build src/QuickBoard/QuickBoard.csproj -c Release
# 产物：src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe
```

零第三方 NuGet 依赖，只用框架自带的 WPF / WinForms / System.Data / System.Text.Json。

## 项目结构

```
src/QuickBoard/
├── App.xaml(.cs)           入口、单实例、探针分发
├── MainWindow.xaml(.cs)    主界面：渲染、键位分发、拖放、右键、增量刷新
├── Theme.xaml              控件样式与主题画刷
├── Core/
│   ├── Models.cs           数据模型 + 键位表（唯一真值源）
│   ├── ConfigStore.cs      加载/保存、原子写入、自愈、备份导入导出
│   ├── Launcher.cs         启动进程、窗口摆位、多屏
│   ├── IconService.cs      图标提取与磁盘缓存
│   ├── IconLoader.cs       专属后台线程加载队列
│   ├── Appearance.cs       主题 / 透明度 / 字号运行时切换
│   ├── OutlinedTextBlock.cs 带描边且支持省略号的文本控件
│   ├── SoftShadow.cs       程序生成阴影，替代 DropShadowEffect
│   ├── EntryClipboard.cs   剪切/复制/粘贴纯逻辑（无 WPF 依赖）
│   └── *Probe.cs,SelfTest.cs 自检与探针
├── Views/                  编辑条目、分组、热键、设置、输入框
└── app.manifest            DPI 感知

tools/                      开发期验证脚本（Python，截图与探针）
HANDOFF.md                  开发交接文档：每轮改动、根因、实测数据
```

## 自检与探针

程序内置一批可重复运行的验证入口，改完不用靠"看起来对"：

```powershell
QuickBoard.exe --selftest  report.txt    # 16 项单元自检
QuickBoard.exe --layoutdump out.txt      # 布局基线（网格宽高、按钮尺寸、行 y 坐标）
QuickBoard.exe --menuprobe dir           # 右键菜单项与禁用态，渲染成 PNG
QuickBoard.exe --launchprobe out.txt     # 窗口摆位能否真正生效
QuickBoard.exe --memprobe out.txt        # 内存分阶段归因
```

探针全程不写用户配置（`SuppressPersist`）。其余探针见 `HANDOFF.md` §8。

## 与原版的关系

QuickBoard 是**干净重写**：没有复制 MadAppLauncher 的任何代码，只兼容它的 `.mal` 配置文件格式以便迁移。原版为 GPLv3，两者的代码库相互独立。

原版做得好、并且值得保留的设计（键名即主键、三层文件分离、图标缓存落盘、窗口摆位）在 `HANDOFF.md` 中有逐项对照；原版的技术债（`BinaryFormatter` 缓存、10×30 硬编码控件、无 DPI 感知）没有复制过来。

## 已知限制

- 仅 Windows；使用 `RegisterHotKey`，与其它占用同一组合键的程序会冲突
- 分组数量无上限，但键盘直达的只有前 10 组（其余用滚轮）
- 未做代码签名，首次运行会有 SmartScreen 提示
- 仓库内不含 `screenshots/`（约 20MB 交付截图），文档中的图片引用只在本机有效
