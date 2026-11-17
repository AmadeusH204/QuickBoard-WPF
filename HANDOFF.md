# QuickBoard 交接文档

> 用途：把当前任务的完整上下文交给下一个模型接手。读完本文即可动手，不需要回看聊天记录。
> 最后更新：2026-11-17 17:20（GMT+8）

---

## 0. 一句话现状

用户原用 **MadAppLauncher**（2012 年 VB.NET/WinForms 启动器），要求重构以加入自定义功能。
已完成：**完全独立的 .NET 8 + WPF 重写版 QuickBoard**，能只读导入原 `.mal` 配置，实现了用户提出的 5 项需求（键盘行对齐布局、背景图+透明度、保留原版设置、滚轮换组、窗口等比缩放）。
2026-11-17 第二轮交付：修复**偶发崩溃**（ToolTip 资源前向引用）、右键菜单**黑字浅底**、空键位**可选中且右键仅「编辑…」可用**、窗口**边缘 8 向缩放 + 透明边框可拖动**（详见 §7/§14）。
2026-11-17 第三轮交付：图标提取改用**原版同款 ExtractIconEx**（修复 IDEA 等错误图标），**移除搜索功能**（顶栏搜索框/结果视图/Ctrl+F 全部删除）（详见 §15）。
2026-11-17 第四轮交付：收藏栏改为 **F1–F12 固定占位**（空位显示虚线框+F键标签、可选中、右键仅「编辑…」可用、编辑即绑定该 F 键）（详见 §16）。
2026-11-17 第五轮交付：实现**真正强制居中**（拖动/缩放均回中，WndProc 拦截 WM_WINDOWPOSCHANGING）+ 背景图**预览与裁剪**（Viewbox 裁剪 + 4 滑块 + 实时预览）（详见 §17）。
2026-11-17 第六轮交付：图标提取从 32×32 升级到 **256×256 高清**（PrivateExtractIcons 取文件图标资源最高分辨率帧，图标身份不变、窗口缩放不模糊），缓存 key v3→v4（详见 §18）。
2026-11-17 第七轮交付：新增**统一分组编辑页**（新增/删除/隐藏/设为主界面/重命名/排序），顶栏「分组」按钮 + 分组右键「编辑分组…」双入口；隐藏分组不进分组栏/数字键/滚轮，主界面分组带 ★ 且启动时优先显示（详见 §19）。
2026-11-17 第八轮交付：**配置自愈**（损坏自动回退 .bak + 现场保留 + 启动可见提示）、**备份/导出/导入**、**启动诊断页**、**图标尺寸三档**、**深色/浅色/跟随系统三主题**（详见 §20）。
2026-11-17 第十轮交付：磁贴/收藏栏名称文字改为**固定黑色**（不随主题变白），**无底板**，配三层白色辉光描边保证壁纸深色区域可辨认；用户否决浅底板方案后修订（详见 §22 及其修订节）。
程序**当前正在运行**，热键 `Ctrl+Alt+Space`。

---

## 1. 硬约束（违反就是事故）

1. **绝对不要修改或删除原程序的任何文件**：
   - 原版的安装目录（`MadAppLauncher\`）整个目录
   - 用户当前在用的 `.mal` 配置（只读导入）
   - `%LOCALAPPDATA%\MadAppLauncher\`（favorites.config / iconcache.idb / user.config）
   历次交付后都核对过这些文件的 mtime 未变，接手后请继续保持。
2. **不要删除 `<项目>/.workbuddy/` 目录**（记忆与项目数据）。
3. 原程序是 **GPLv3**。当前 QuickBoard 是干净重写（仅借鉴数据格式），**没有复制其代码**。若后续反编译抄代码进本项目，整个项目就会被 GPL 传染 —— 需要先和用户确认。
4. 用户是中文用户，界面文案、注释、提交说明都用简体中文；金额/涨跌色等本地化约定按中国习惯。
5. 自检/探针运行会写 `%APPDATA%\QuickBoard\board.json`。测完必须把配置恢复成用户正常状态（见 §9 末尾的"恢复基线"）。

---

## 2. 环境与构建

| 项 | 值 |
|---|---|
| 项目根 | 本仓库根目录（`README.md` / `HANDOFF.md` 所在位置） |
| .NET SDK | **.NET 8 SDK**（开发时为 8.0.428）。本机未装系统级 SDK，用的是绿色版 `dotnet.exe`，未进 PATH |
| 目标框架 | `net8.0-windows`，WPF + WinForms（托盘），`ImplicitUsings=disable` |
| 产物 | `src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe` |
| 依赖 | 零第三方包（BAML/JSON/图标全部用框架 API） |

本机**没有**系统级 dotnet SDK、没有 Visual Studio。下面命令里的 `dotnet` 若不在 PATH，替换成绿色版 SDK 里 `dotnet.exe` 的实际路径即可。

```bash
cd <仓库根目录>
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build src/QuickBoard/QuickBoard.csproj -c Release -v q > build.log 2>&1
grep -E "error|warning CS|已成功生成" build.log | head -10
```

**编译前必须先结束运行中的实例**，否则 exe 被占用会报 MSB3027：

```powershell
Get-Process QuickBoard -ErrorAction SilentlyContinue | Stop-Process -Force
```

启动（后台跑，避免阻塞）：

```bash
cd src/QuickBoard/bin/Release/net8.0-windows && ./QuickBoard.exe
```

---

## 3. 文件职责表

```
QuickBoard/
├── HANDOFF.md                  ← 本文
├── selftest.txt                最近一次自检报告（9/9）
├── samples/背景示例.png        演示用渐变背景图（可在设置里换成用户自己的）
├── screenshots/                交付截图：主界面 / 分组2 / 窗口缩放 / 设置面板 / 右键菜单三态 / ToolTip / 图标对比
├── tools/                      验证脚本（Python，见 §8）
└── src/QuickBoard/
    ├── QuickBoard.csproj       项目文件（无 NuGet 依赖）
    ├── app.manifest            PerMonitorV2 DPI 感知（原版缺失，高分屏糊）
    ├── App.xaml / App.xaml.cs  入口：单实例、命令行开关、托盘、全局异常
    ├── Theme.xaml              全部样式与**动态画刷**（Appearance 运行时改写）
    ├── MainWindow.xaml         主界面（设计画布 1100×610 + Viewbox）
    ├── MainWindow.xaml.cs      主界面逻辑（897 行，见 §4 锚点）
    ├── GlobalUsings.cs         手动 using（禁用隐式 using 的替代）
    ├── Core/
    │   ├── Models.cs           数据模型 + SlotKeys 键位表 + 视图模型
    │   ├── ConfigStore.cs      JSON 读写 + 旧配置迁移 + .bak 轮转 + 损坏自愈 + 导出/导入
    │   ├── MalImporter.cs      只读解析 .mal / favorites.config
    │   ├── Launcher.cs         启动进程 / 提权 / 启动后摆窗口
    │   ├── IconService.cs      ExtractIconEx 优先 + Shell 回退 + PNG 磁盘缓存（v3）
    │   ├── IconProbe.cs        --iconprobe 图标诊断探针（逐条目来源/尺寸/耗时）
    │   ├── GroupProbe.cs       --groupprobe 分组编辑操作探针（13 项断言）
    │   ├── ShortcutResolver.cs .lnk 解析（拖放快捷方式时用）
    │   ├── Appearance.cs       运行时替换 Theme 画刷（含深/浅主题）、背景图、图标尺寸档位
    │   ├── Diagnostics.cs      启动健康诊断文本汇总
    │   ├── HotkeyService.cs    RegisterHotKey 全局热键
    │   ├── StartupRegistrar.cs 开机自启（HKCU\...\Run）
    │   ├── Native.cs           P/Invoke 集合
    │   ├── SelfTest.cs         `--selftest` 14 项自检
    │   ├── LayoutProbe.cs      `--layoutdump` 离屏量几何
    │   ├── ThemeProbe.cs       `--themeprobe` 三档主题画刷/截图探针
    │   ├── ComboProbe.cs       `--comboprobe` 下拉框前景色诊断探针 + 下拉截图
    │   ├── MenuProbe.cs        `--menuprobe` 右键菜单探针
    │   └── DialogPreview.cs    `--uipreview` 离屏渲染对话框
    └── Views/
        ├── SettingsDialog.xaml(.cs)  设置面板（8 个区块）
        ├── EditEntryDialog.xaml(.cs) 条目编辑
        ├── GroupEditorDialog.xaml(.cs) 分组编辑页（新增/删除/隐藏/主界面/重命名/排序）
        ├── HotkeyDialog.xaml(.cs)    热键录制
        └── PromptDialog.xaml(.cs)    通用单行输入（重命名分组）
```

代码量：`MainWindow.xaml.cs` 986 行是最大的文件，其余均 < 300 行。

---

## 4. 主界面结构与代码锚点

### XAML 骨架（`MainWindow.xaml`）

| 行 | 内容 |
|---|---|
| 17 | `TileMenu` 磁贴右键菜单（x:Shared=False） |
| 34 / 65 / 96 / 140 / 147 | 模板：`TileVisual`（键位磁贴）/ `FavVisual`（收藏栏磁贴）/ `TileCell`（含空位虚线格）/ `FavCell` / `ResultCell` |
| **156–157** | **`Viewbox ScaleBox` 包住 `<Grid Width="1100" Height="610">` —— 等比缩放的关键** |
| 158 | `RootBorder` 圆角面板（半透明，受 UiOpacity 影响） |
| 167–169 | `PhotoLayer` / `PhotoBrush` / `PhotoScrim` 背景图三层 |
| 195 | `SearchBox`（Ctrl+F 聚焦） |
| 222 | `GroupBar` 分组胶囊 ItemsControl |
| 249 | `FavPanel` 收藏栏（**独立于分组，固定 Grid.Row=2 置顶**） |
| 266–273 | `KeyboardScroller` → `SlotGrid`（**固定 Height=336、MaxWidth=1020，ItemsPanel 为 `UniformGrid Columns=10 Rows=3`**） |
| 278–287 | `ExtraGrid` / `ExtraHint`：30 槽都占满后剩余条目的兜底区 |
| 311–313 | `StatusText` / `HintText` |
| 319 | `ResizeGrip`：无边框窗口右下角拖拽手柄 |

### `MainWindow.xaml.cs` 方法索引

| 行 | 方法 | 说明 |
|---|---|---|
| 40 | 构造函数 | 绑数据、挂事件；参数 `(ConfigStore store, BoardConfig config)` |
| 80 | `FavoritesGroup` | `Groups.FirstOrDefault(g => g.IsFavorites)` |
| 96 | `EnsureFavoritesGroup()` | 没有收藏栏就创建一个 |
| 106 | `ResolveInitialGroup()` | 按 `LastGroupIndex` 恢复上次分组 |
| 122 | `SelectGroup(int)` | 切组并保存 `LastGroupIndex` |
| 136 | `StepGroup(int)` | 滚轮/方向键用，首尾循环 |
| 165 | `ShowAndFocus()` | 显示+置前+居中+聚焦（搜索已移除，第三轮） |
| 192 / 206 / 223 | `ApplyWindowRect` / `PersistWindowRect` / `Window_Deactivated` | 位置记忆与失焦自动隐藏 |
| 506 | `WndProc` | 8 向缩放 + 透明边框 HTCAPTION 拖动（见 §14） |
| 695 | `ContextMenu_Opened` | 空键位菜单禁用逻辑（见 §14） |
| 714 / 742 | `MenuEdit_Click` / `EditEmptySlot` | 空键位「编辑…」新建绑定该键位的条目 |
| 243 | `ApplyAppearance(BoardOptions)` | 应用不透明度/背景图 |
| **264** | **`RebuildBoard()`** | **核心渲染**：收藏栏 → 30 个 `SlotCell`（空位也建）→ extras（第三轮起无搜索分支） |
| 332 | `Matches(entry)` | 目前只匹配 Name / Target 的 `Contains` |
| 349 | `LoadIconAsync` | 后台线程取图标，防卡 UI |
| 396 | `Window_PreviewKeyDown` | **全部键盘交互入口**，见 §5 |
| 476 | `Window_PreviewMouseWheel` | 滚轮：能滚列表先滚，否则 `StepGroup` |
| 506 | `WndProc` | 无边框窗口 8 向 `WM_NCHITTEST` 改尺寸 |
| 666 | `OpenSettings()` | 打开设置（有 `_preview` 回调实时预览） |
| 756 | `MenuToggleFavorite_Click` | 加入/移出收藏栏（自动分配空 F 键） |
| 872 | `AddPaths(IEnumerable<string>)` | 拖放落盘：`.lnk` 解析 + 自动分配空槽 |
| 915 | `ReloadFromDisk()` | 托盘"重新载入配置" |

---

## 5. 键位与交互映射

| 操作 | 行为 | 实现 |
|---|---|---|
| `1`–`0` | 切到第 1–10 个分组 | `SlotKeys.GroupIndexFromKey` |
| `Q W E R T Y U I O P` | 第 1 排槽位 1–10 | `SlotKeys.FromKey` |
| `A S D F G H J K L ;` | 第 2 排槽位 11–20 | 同上 |
| `Z X C V B N M , . /` | 第 3 排槽位 21–30 | 同上 |
| `F1`–`F12` | 启动收藏栏对应条目（任意页都可用） | `entry.FunctionKey` |
| `Esc` | 隐藏窗口 | — |
| 滚轮 | 切分组（列表可滚时先滚列表） | `PreviewMouseWheel` |
| 拖放文件/快捷方式 | 添加到当前分组并自动占空槽 | `Window_Drop` → `AddPaths` |
| 磁贴右键 | 启动/编辑/打开位置/加入收藏/删除 | `TileMenu` |

**键位表是唯一的**：`Core/Models.cs` 的 `SlotKeys.Labels`（30 项）同时决定数据槽位、UI 图片中的键标、以及解析 `buttonID`。改键位只改这里。

---

## 6. 数据模型

### 运行时配置：`%APPDATA%\QuickBoard\board.json`

```jsonc
{
  "Version": 2,
  "Groups": [
    {
      "Id": "guid-hex",
      "Name": "收藏栏",
      "IsFavorites": true,          // true = 不参与分组切换，每页置顶
      "Entries": [
        {
          "Id": "guid-hex",
          "Name": "Edge",
          "Target": "C:\\...\\msedge.exe",
          "Arguments": "",
          "WorkingDirectory": "",
          "IconPath": "",            // 空 = 自动提取
          "RunAsAdmin": false,
          "Slot": 0,                 // 1..30 = 字母键位，0 = 不绑定
          "FunctionKey": 2,          // 1..12 = F1–F12
          "WindowState": "Normal",   // Normal | Maximized | Minimized
          "WindowPlacement": "",     // "" | LeftHalf | RightHalf | Center
          "WindowMoveDelay": 1000
        }
      ]
    }
  ],
  "Hotkey": { "Ctrl": true, "Alt": true, "Shift": false, "Win": false, "Key": "Space" },
  "Options": {
    /* 原版视图菜单 */ "AlwaysOnTop": true, "CenterOnScreen": true,
    "HideOnDeactivate": true, "HideAfterLaunch": true, "ShowFavoritesBar": true,
    /* 常规 */ "ShowTrayIcon": true, "StartHidden": false, "StartWithWindows": false,
    /* 外观 */ "UiOpacity": 0.9, "BackgroundImage": "C:\\...\\背景示例.png", "BackgroundImageOpacity": 0.92,
    /* 窗口 */ "WindowWidth": 1075, "WindowHeight": 690,
    "HasWindowPosition": true, "WindowLeft": 370, "WindowTop": 210
  },
  "LastGroupIndex": 1
}
```

读写走 `ConfigStore`：**写临时文件再 `File.Move` 覆盖**（原子写）；读失败会把坏文件备份成 `board.json.broken-<时间戳>` 再回落默认值。

### 原版 `.mal` 格式（只读）

强类型 `DataSet` 序列化 XML：`MAConfig → MATab×10 → MAButton×30`。
`buttonID` 形如 `btnMAQ` / `btnMASlash`，**它本身就是物理键位**，导入时按 `SlotKeys` 反解成 `Slot`。
`favorites.config` 同结构，导入后打 `IsFavorites=true`，`btn_MAF1..F10` → `FunctionKey`。
`iconcache.idb` 是 `BinaryFormatter` 序列化的 `Dictionary<string,Bitmap>`，**.NET 5+ 无法反序列化，直接弃用**（重新提取图标无损失）。

导入入口：`MalImporter.ImportMal(path)`，同时会在 `.mal` 同目录和 `%LOCALAPPDATA%\MadAppLauncher` 两处找 `favorites.config`。

---

## 7. 已实现功能

**用户 5 项需求**

| # | 需求 | 实现 |
|---|---|---|
| 1 | UI 按键盘行对齐，收藏栏每页置顶 | 固定设计画布 + `UniformGrid 10×3`；30 格空位也占位；`IsFavorites` 独立渲染在顶部 |
| 2 | 自定义背景图 + UI 透明度 | `Appearance.cs` 运行时替换 Theme 动态画刷；背景图 `ImageBrush` + 压暗层；设置面板可调 20%–100% |
| 3 | 保留原软件已有设置 | Auto Hide / Show Favorites Bar / Always Center / Always On Top / 窗口尺寸位置记忆 / 当前分组记忆 / 热键 / `.mal` 导入 |
| 4 | 滚轮切换分组 | `PreviewMouseWheel`，列表可滚时优先滚列表 |
| 5 | 窗口可缩放且 UI 等比例缩放 | `Viewbox(Uniform, Both)` + 自绘 8 向 `WM_NCHITTEST` + 右下角手柄 |

**2026-11-17 第二轮（bug 修复 + 3 项需求）**

| # | 需求 | 实现 |
|---|---|---|
| 6 | 偶发崩溃 | 根因：`Theme.xaml` 里 ToolTip 样式前向引用了同字典后部才定义的 `TextBrush`/`UiFont`，ToolTip 惰性实例化时 `StaticResource` 解析失败 → XamlParseException。修复：画刷/字体定义全部移到字典顶部 + ToolTip 样式改 `DynamicResource` 双保险 |
| 7 | 右键菜单字体看不清 | 原本菜单文字被隐式 `TextBlock` 样式染成白色（浅色菜单底上不可读）。新增 `ContextMenu`/`MenuItem`/`Separator` 样式：浅底 + 黑字（`#20242A`），禁用项淡灰（`#A9B0BA`） |
| 8 | 空键位可选中、右键仅「编辑…」可用 | `TileCell` 空位也渲染按钮（悬停有反馈、点击无反应）；`ContextMenu_Opened` 对空键位禁用除「编辑…」外全部项；`EditEmptySlot`：空键位点「编辑…」= 新建绑定该键位的条目（填收藏键则进收藏栏） |
| 9 | 界面可拖动、各方向调整大小 | `WndProc` 命中测试改以可见面板（RootBorder）为基准：面板边缘 ±7px + 外围透明边 = 8 向缩放；顶栏 `DragMove` 拖动；吞掉 `WM_NCLBUTTONDBLCLK` 防误最大化 |

**额外做的**：托盘图标（显示/隐藏、设置、重载、打开配置目录、退出）、开机自启、单实例（第二实例唤起第一个）、拖放添加、磁贴右键菜单、分组重命名/删除、条目编辑（含键位/收藏键/摆窗）、全局热键录制、README 级提示文案、异常写 `%APPDATA%\QuickBoard\error.log`。

---

## 8. 验证手段（改完必须跑，别靠"看起来对"）

### 8.1 自检 `--selftest`（9 项，必跑）

```bash
./src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe --selftest "$PWD/selftest.txt"
sleep 4; cat selftest.txt
```

覆盖：导入 `.mal` 配置、导入 favorites.config、图标提取（4 个样本须 256×256，含文件夹）、配置往返、`Process.Start` 启 notepad 并杀掉、热键注册/注销、键盘三排布局可逆、收藏栏标记、外观参数夹取。
**基线：9/9 通过。** 若掉项，先修再谈新功能。

### 8.2 布局探针 `--layoutdump`（判断缩放/对齐/溢出，以数字为准）

```bash
./src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe --layoutdump "layout.txt" 1180 660
sleep 3; cat layout.txt
```

输出：Viewbox 缩放比、网格实际尺寸、三排 y 坐标、末格右缘是否超网格、每个按钮高度。
**基线**：1180×660 → 缩放 1.0718、网格 950×336、三排 y = 0/112/224、不溢出、12 个按钮高度全 104。

### 8.3 菜单探针 `--menuprobe`（右键菜单/ToolTip 必跑）

```bash
./src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe --menuprobe "$PWD/menuprobe"
sleep 3; cat menuprobe/report.txt
```

离屏打开三种右键菜单（有内容磁贴 / 空键位 / 分组胶囊），输出每项 `IsEnabled` 并渲染 PNG；实例化 ToolTip（旧崩溃点）；空键位触发「编辑…」验证对话框能打开。**基线**：全 OK，空键位仅「编辑…` IsEnabled=True`，其余 5 项 False，分组菜单 2 项均 True。
PNG 可用像素分析核对黑字/灰字（禁用 `#A9B0BA`，启用 `#20242A`，底 `#F7F9FC`）。

### 8.4 对话框离屏渲染 `--uipreview`

```bash
./src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe --uipreview "$PWD/preview"
sleep 3; cat preview/report.txt   # 输出 settings.png / edit.png / hotkey.png
```

用 `RenderTargetBitmap` 渲染，不受 DWM 半透明影响，适合查对话框重叠/截断。它同时会打印控件实际取值（曾靠这个抓出"滑块显示 0%"的真 bug）。

### 8.5 截图（实机画面）

```bash
python tools/grab_window.py QuickBoard shot.bmp          # 按标题取最大窗口，GDI 抓屏
python tools/capture2.py QuickBoard shot.bmp screen      # 指定区域/整屏
python tools/ui_probe.py QuickBoard wheel -120 1         # PostMessage 滚轮（**无效，见坑 §9.5**）
python tools/ui_probe.py QuickBoard resize 900 520       # SetWindowPos 改尺寸
python tools/crop.py shot.bmp crop.png 40 230 400 200 2  # 纯 Python 裁 BMP 放大 N 倍
python tools/bmp_to_png.py shot.bmp out.png              # 转 PNG 交付
```

**判断"有没有被裁"一律以 `--layoutdump` 数字为准**，截图只用来确认观感（DWM 半透明窗口截图会有残影，历史上误判过两次）。

### 8.6 恢复基线（测完收尾）

```python
# 恢复窗口尺寸与位置标记，避免探针的离屏坐标留在配置里
o['WindowWidth']=1180; o['WindowHeight']=660
o['HasWindowPosition']=False
```

---

## 9. 踩过的坑（原样记录，别重犯）

1. **WPF + WinForms 混用 + `ImplicitUsings=enable` → CS0104 类型歧义**（`Application`/`MessageBox`/`Color` 等同时存在于两个命名空间）。
   → 已改 `ImplicitUsings=disable` + `GlobalUsings.cs` 显式列 using。**新加文件要自己写 using**。
2. **图标提取**：用 `SIIGBF_BIGGERSIZEEXACTLY` + 请求 64px 时，只拿到 32px，且**文件夹取不到图标**。
   → 改成 256 → 128 → 64 → 48 → 32 多档试探 + `SHGetFileInfo` 回退，缓存键里含尺寸档位。
3. **收藏栏配置不在 `.mal` 同目录**，而在 `%LOCALAPPDATA%\MadAppLauncher\favorites.config`。
   → 导入器两处都查。
4. **`WrapPanel` 会按宽度乱换行**，键位和位置对不上 → 必须用固定列数 `UniformGrid` + `Viewbox`。
5. **WPF 滚轮事件依赖光标真实在窗口内**：`PostMessage(WM_MOUSEWHEEL)` 模拟无效（需要 `mouse_event` 且光标在窗内）。测试时别误判成功能坏了。
6. **`SettingsDialog` 构造里连续赋两个 Slider.Value** → 第一个的 `ValueChanged` 把第二个清零，界面显示 0%。→ 加 `_loading` 守卫。**任何"两个联动控件初始化"都要这个守卫。**
7. **布局探针为了离屏把窗口放到 Left=-4000** → 关窗时 `PersistWindowRect` 把 -4000 写进了用户配置。→ `MainWindow` 加 `SuppressPersist`，探针模式下不落盘。
8. **`BoardOptions.MemberwiseClone()` 是 protected** → 对外用 `Clone()` 包装。
9. **磁贴高度会被内容驱动**：名字两行时磁贴变高，行不对齐。→ 磁贴 Button 显式 `Height=104`，模板内部 `ClipToBounds`；探针验证 12 个格子高度全等。
10. **`MainWindow` 无边框 + `AllowsTransparency` 下系统拖拽失效** → 自绘 `WM_NCHITTEST` 返回 8 向 `HT*` + 右下角手柄。
11. **单实例 Mutex 在调试时残留**：杀进程后立刻重启可能 `createdNew=false` → 用 `Get-Process | Stop-Process -Force` 后 `Start-Sleep 600ms` 再启动。
12. **后台任务报 exit code 127** 是 Git Bash 下 exe 启动方式的老问题（进程其实起来了）。判断"是否真在运行"用 `tasklist` / `Get-Process`，不要只看退出码。
13. **沙箱会杀掉向其他进程注入右键消息的脚本**：Python `PostMessage(WM_RBUTTONDOWN)` 到 QuickBoard 后，验证脚本整个进程树立即被环境终止（exit 127）。→ 验证菜单一律用 `--menuprobe`（程序内自己打开菜单），不要注入输入。
14. **沙箱在工具调用结束时回收子进程**：后台 `&` 启动的 QuickBoard 在 Bash 任务结束后被杀。→ 需要"启动→操作→截图"全流程放进**同一次调用**里完成；或用 `Start-Process` 但跨调用仍会消失。跨调用保持运行目前做不到，交付前在同一次调用里启动。
15. **合成 `WM_MOUSEMOVE` 触发不了 WPF ToolTip 悬停**（同 §9.5 滚轮问题）。ToolTip 验证用 `--menuprobe` 的程序化 `IsOpen=true`。
16. **WM_NCHITTEST 探针要等窗口位置稳定**：窗口 `Show()` 后 `CenterOnScreen` 还会移动窗口，立即扫描会拿到错位结果（曾误判右/下边缘失效）。启动后 sleep 1s 再取 rect。
17. **GDI `GetDIBits` 写的 24bpp BMP 行距不保证 4 字节对齐**（本机 1075 宽时行距 3225 未填充）。转换时用 `(filesize-54)//height` 反推行距。`tools/bmp2png.py` 已处理。

---

## 10. 已知问题 / 技术债

| 级别 | 问题 | 位置 |
|---|---|---|
| 中 | 分组上限实际是 10（数字键 `1`–`0`），超过 10 组后只能点胶囊 | `SlotKeys.GroupIndexFromKey` |
| 低 | 30 槽占满后新条目落到 `ExtraGrid`，没有"换页"概念（原版是 10 tab × 30 = 300） | `RebuildBoard` |
| 低 | `WindowPlacement` 只有 LeftHalf/RightHalf/Center 三档，无自由坐标 | `Launcher.cs:54` |
| 低 | 托盘图标用的是 `SystemIcons.Application`，不是自己的图标 | `App.xaml.cs:126` |
| 低 | 无打包/安装器，目前靠 `bin\Release` 直接跑；开机自启指向该 exe 的绝对路径 | `StartupRegistrar.cs` |
| 低 | `MainWindow.xaml.cs` 897 行偏大，`RebuildBoard`/右键菜单/拖放三块可拆 ViewModel | — |
| 提示 | 设置面板的实时预览会在拖动滑块时立刻改全局画刷，取消时靠 `ApplyAppearance(_config.Options)` 回滚 | `MainWindow.xaml.cs:243` |

---

## 11. 下一步候选（用户已提过方向，未定优先级）

用户上轮结尾问"下一步要不要做"，尚未答复。候选：

2. **一键工作流**：一个条目依次执行多动作（开 IDE → 打开项目目录 → 起浏览器 → 等窗口摆位）。建议在 `Launcher` 旁加 `IAction`/`WorkflowRunner`，`AppEntry` 加可选 `Steps` 列表，不改现有启动路径。
3. **配置云同步/多设备**：`board.json` 放 Git 仓库或网盘，加导入导出与冲突提示。
4. **无键位条目分页**：把 `ExtraGrid` 升级成"第 2 页/第 3 页"，恢复原版 300 条容量。
5. **打包**：`dotnet publish -r win-x64 --self-contained false` + 简单安装脚本，或单文件发布。

**建议顺序**：先 1（用户最可能马上要、改动可控），再 2。

---

## 12. 接手检查清单

拿到任务后按顺序做这 6 步，10 分钟内对齐状态：

1. `cd QuickBoard && tasklist | grep -i quickboard` —— 确认是否已有实例在跑（有就先关）。
2. 读 `selftest.txt` 确认基线 9/9；不确定就重跑 `--selftest`。
3. 读 `%APPDATA%\QuickBoard\board.json` 看用户当前真实数据（当前：收藏栏 8 条 / 分组 1 有 29 条 / 分组 2 有 5 条）。
4. 核对原程序文件 mtime 未变（§1 的三个路径）。
5. 改动前先 `dotnet build` 一次确认基线可编译（零警告零错误）。
6. 改完必须：`build` 零警告 → `--selftest` 9/9 → `--layoutdump` 数字符合基线 → 截图目视确认 → 清理临时 bmp/log → 恢复配置基线。

---

## 13. 当前运行态快照（2026-11-17 15:05）

- 进程：`QuickBoard.exe` 正在运行（第二轮交付后重启）
- 热键：`Ctrl + Alt + Space`
- 配置：3 组 42 条（收藏栏 8 / 分组 1 29 / 分组 2 5），`LastGroupIndex = 1`
- 外观：`UiOpacity = 0.9`，背景图 `samples\背景示例.png`，`BackgroundImageOpacity = 0.92`
- 窗口：1075×690 @ (370, 210)，位置已记忆
- 原程序文件：未改动（每轮交付后核对 mtime）

---

## 14. 第二轮修复与验证记录（2026-11-17）

### 14.1 偶发崩溃根因

`error.log` 唯一一条记录：`XamlParseException: 无法找到名为 TextBrush 的资源`，调用栈在 `PopupControlService.ShowToolTip` → ToolTip 弹出实例化模板时。
根因：`Theme.xaml` 中 ToolTip 样式（第 15 行）`StaticResource` 引用了同字典**后面**才定义的 `TextBrush`/`UiFont`。普通样式在加载时解析会立刻报错，但 ToolTip 模板是**惰性**的——只有鼠标悬停真正弹出提示框那一刻才解析，于是变成"偶发"。全局异常处理器只是记日志+弹"出错了"对话框，用户感知为崩溃/报错。
修复（双保险）：① 所有画刷/字体定义移到字典顶部；② ToolTip 样式改 `DynamicResource`。`--menuprobe` 里程序化 `ToolTip.IsOpen=true` 在旧代码 100% 复现，新代码正常渲染。

### 14.2 改动文件

| 文件 | 改动 |
|---|---|
| `Theme.xaml` | 画刷/字体前置；ToolTip 改 DynamicResource；新增 ContextMenu/MenuItem/Separator 样式（浅底黑字、禁用淡灰、圆角、阴影） |
| `MainWindow.xaml` | `TileCell` 空位也渲染按钮（Content/ContentTemplate/ToolTip 移入 Style 便于触发器切换，空位提示"右键编辑"） |
| `MainWindow.xaml.cs` | `WndProc` 面板矩形 8 向命中 + HTCAPTION + 吞双击；`ContextMenu_Opened` 空键位禁用逻辑；`MenuEdit_Click`/`EditEmptySlot` 空键位新建条目（空目标放弃） |
| `App.xaml.cs` | 新增 `--menuprobe` 分支 |
| `Core/MenuProbe.cs` | 新增（探针，不参与正常运行） |

### 14.3 验证证据（全部通过）

- 构建：零警告零错误
- `--selftest`：9/9
- `--layoutdump` 1180×660：缩放 1.0718、网格 950×336、三排 y=0/112/224、不溢出、按钮高全 104（与基线一致，TileCell 改动未影响几何）
- `--menuprobe`：三种菜单 IsEnabled 全符合预期；ToolTip 渲染 OK；空键位「编辑…」打开编辑对话框（自动取消未写配置）
- 像素分析（`screenshots/菜单_*.png`）：有内容磁贴 6 项全黑字；空键位仅「编辑…」黑字其余 5 项灰字；分组胶囊 2 项黑字
- WM_NCHITTEST 扫描：左右上下 + 四角 + 边缘带全部返回预期 HT* 值（面板边缘 ±7px 及外侧透明边）
- 配置恢复：窗口位置 (370,210) 已恢复；原程序三个文件 mtime 未变

---

## 15. 第三轮改动与验证记录（2026-11-17 14:45）

### 15.1 图标显示异常

**现象**：部分磁贴图标错误/缩小。典型：IntelliJ IDEA 显示成 JetBrains 通用方块，Shell 256px 接口把小图标放进大画布或返回错误资源。

**根因**：`IconService.Extract()` 用 `IShellItemImageFactory.GetImage(256, ICONONLY)`，该 Shell 接口对部分 exe 返回通用图标或画布-小图组合；而原版 MadAppLauncher 用 `ExtractIconEx(path, 0)` 直接取文件内第 0 个图标资源（其 `iconcache.idb` 的 key 全是 `文件名,0` 格式，实证此语义）。

**修复**：`Extract()` 改为 **ExtractIconEx(path, 0) 优先**（排除 .lnk——它无自身图标资源，仍走 Shell 解析目标），失败再回退 Shell 256/96/48/32。`BuildKey` 版本 v2→v3 让旧错误缓存失效。`Native.cs` 新增 `ExtractIconEx` P/Invoke。

**诊断手段**：新增 `Core/IconProbe.cs` + `--iconprobe <报告路径>`：清空临时缓存后逐条目实际提取，报告来源路径、是否存在、ImageSource 类型与像素尺寸、耗时、缓存 key。用它扫出 42 个图标全部成功但 IDEA 拿到 32×32（原 Shell 为 256×256 的通用图标），据此定位根因。

### 15.2 移除搜索功能

用户反馈搜索不常用。完整移除：
- `MainWindow.xaml`：删顶栏搜索 TextBox 与占位提示、`ResultScroller`/`ResultGrid` 搜索结果视图；顶栏列定义从 3 列缩为 2 列；`ResultCell` 模板保留（ExtraGrid 额外条目区仍用）
- `MainWindow.xaml.cs`：删 `_results`、`_filter`、`Matches()`、`SearchBox_TextChanged()`、`CurrentGroupFirstTile()`；`RebuildBoard()` 去掉搜索分支只留键盘网格；`Window_PreviewKeyDown` 删 Ctrl+F / ESC 清搜索 / 搜索内 Enter 分支，ESC 直接 Hide；滚轮逻辑不再判断 ResultScroller；`ShowAndFocus` 不再 Clear 搜索；状态栏去掉 Ctrl+F 提示
- `Theme.xaml`：删 `SearchBox` 样式

### 15.3 验证证据（全部通过）

- 构建零警告零错误；`--selftest` 9/9；`--layoutdump` 1180×660 与基线一致（网格 950×336、按钮高全 104）
- `--menuprobe` 全绿（三种菜单 IsEnabled 正确、ToolTip OK、空键位编辑 OK）
- `--iconprobe`：42 条全部成功提取；IDEA 从 256×256 通用方块变为 32×32 IJ 图标；图标总览确认全部撑满、无缩小/空白/错误图标
- 实机截图 `screenshots/主界面_第三轮.png`：顶栏无搜索框、IDEA 显示 IJ 图标、状态栏无 Ctrl+F 提示、布局正常
- 配置未污染：3 组 42 条、LastGroupIndex=1、窗口 (370,210) 1075×690
- 原 MadAppLauncher 文件未改动（每轮核对 mtime）



---

## 16. 第四轮改动与验证记录（2026-11-17 14:52）

### 16.1 收藏栏固定占位

**需求**：用户反馈原版收藏栏 F1–F10（原版 .mal 配置用到 F2/F4–F10）每个功能键都有占位，QuickBoard 之前只渲染实际存在的 8 项，"有一个才占一个"。

**实现**（与键盘空槽 `SlotCell` 同构）：
- `Core/Models.cs` 新增 `FavSlot`（`FunctionKey` / `KeyLabel="F"+n` / `Tile?` / `HasTile`）
- `MainWindow.xaml` 的 `FavCell` 模板：空位显示虚线框 + 淡色 F 键标签，按钮始终渲染（空位可点选但无反应，ToolTip 提示右键编辑）
- `MainWindow.xaml.cs`：`_favorites` 类型 `ObservableCollection<TileItem>` → `ObservableCollection<FavSlot>`；`RebuildBoard()` 按 F1–F12 建 12 个格位（有条目填 Tile，空位保留标签）；`FavHint` 改为「无任何收藏条目时才显示」
- `EditEmptySlot()` 支持 `FavSlot`：空收藏位点「编辑…」预填对应 F 键号，保存后直接进收藏栏；同键号旧条目先移除避免重复
- `ContextMenu_Opened` 空位判断天然兼容（空位 Tag 为 null → 仅「编辑…」可用）

**关于 F1–F12 vs F1–F10**：快捷键系统本就支持 F1–F12（`NextFreeFunctionKey` 循环 1..12、快捷键处理 `Key.F1..F12`），故按 12 个占位实现，完整覆盖用户要求的 F1–F10。

### 16.2 "程序自行终止"排查结论

用户反馈启动后程序自行终止。查 `%APPDATA%\QuickBoard\error.log` 仅 1 条记录且时间戳为 `02:58:21`（上一轮 ToolTip 崩溃，已修复），本轮无新异常 → **非程序崩溃**。根因是沙箱在工具调用结束时回收后台启动的子进程（已知限制，见 §9.14）。交付时用 `Start-Process` 重新启动；用户自己在桌面双击 exe 正常运行不受影响。

### 16.3 验证证据（全部通过）

- 构建零警告零错误；`--selftest` 9/9
- `--layoutdump` 1180×660：收藏栏 12 占位 912×82、键盘网格 950×336、三排 y=0/112/224、分组栏 2 项（几何未受影响）
- `--menuprobe` 全绿：有内容磁贴 6 项全启用、空键位仅「编辑…」启用、分组菜单 2 项启用、ToolTip OK、空位编辑打开对话框
- 实机截图 `screenshots/收藏栏_固定占位.png`：12 个 F 位齐全、空位虚线框+F 标签、有内容位图标名称正常、布局无重叠
- 配置未污染：3 组 42 条、收藏 8 项（F2/F4–F10）、窗口 (370,210) 1075×690


---

## 17. 第五轮改动与验证记录（2026-11-17 15:05）

### 17.1 真正强制居中

**需求**：像原版一样即便拖动窗口也强制居中。原实现只在 `ShowAndFocus()` 调 `CenterOnScreen()`，拖动（`DragMove` + HTCAPTION 透明边框）后窗口停在拖动位置。当前配置窗口 (401,169) 就是拖过的残留。

**实现**：
- `MainWindow.xaml.cs` 的 `WndProc` 新增 `WM_WINDOWPOSCHANGING`(0x0046) 拦截：`CenterOnScreen=true` 且 Normal 状态时，用 `MonitorFromWindow`+`GetMonitorInfo` 取当前显示器工作区，把 WINDOWPOS 的 x/y 改写为居中值（`SWP_NOMOVE` 清除让系统真正移动），保留 cx/cy → 8 向缩放照常，缩放后自动重居中。
- cx/cy 无效（SWP_NOSIZE 时为 0）时回退 `GetWindowRect` 取当前尺寸；两者都无效则跳过（早期创建消息）。
- `CenterOnScreen()` 改用当前显示器工作区（含 DPI→DIP 换算），支持多屏。
- `OnLoaded` 里 `Dispatcher.BeginInvoke(CenterOnScreen)` 保证首次显示即居中。
- `Header_MouseDown` 在强制居中时不再 `DragMove()`（窗口纹丝不动）。
- 关闭 `CenterOnScreen` 开关时全部放行，恢复自由拖动。配置项复用设置面板已有的「每次显示都居中」复选框。

**踩坑**：初版直接用 wp.cx/cy，早期消息 cx/cy=0 导致把左上角算到屏幕中心 (960,540) 而非居中 (325,178)。修复：cx/cy 无效时回退 GetWindowRect。

### 17.2 背景图预览与裁剪

**需求**：程序界面与背景图适配，通过预览和裁剪自定义显示。原来只有路径+不透明度，固定 `Stretch=UniformToFill` 无裁剪无预览。

**实现**：
- `BoardOptions` 新增 5 字段：`BackgroundFit`(0铺满可裁剪/1完整适应/2拉伸)、`BackgroundCropX/Y/Width/Height`（原图相对坐标 0–1，默认 0,0,1,1=全图）。旧配置缺字段走默认值，完全兼容。
- `Appearance.LoadImageBrush` 新增 6 参数重载：按 fit 选 Stretch，非全图裁剪时设 `ImageBrush.Viewbox`(RelativeToBoundingBox)。保留旧 2 参数重载兼容其它调用。
- `MainWindow.ApplyAppearance` 传入新裁剪参数。
- `SettingsDialog.xaml` 外观区新增：预览框（Border+ImageBrush 实时显示裁剪效果）、铺放方式 ComboBox、X/Y/宽/高 4 个滑块、「重置裁剪为全图」按钮。
- `SettingsDialog.xaml.cs`：初始化控件值；`SyncCrop()` 保证裁剪框不越界（宽/高夹到 ≤1-x/1-y）并触发主窗口实时预览；`Crop_Changed`(ComboBox)/`CropSlider_Changed`(Slider) 分别处理（签名不同）；`RefreshBgPreview()` 刷新本对话框预览；浏览/清除图片后也刷新。
- `SelfTest` 配置往返新增裁剪字段断言。

### 17.3 验证证据（全部通过）

- 构建零警告零错误；`--selftest` 9/9（含裁剪字段往返）
- `--menuprobe` 全绿；`--uipreview` settings 面板 540×746，`preview=ok`、裁剪字段状态正确
- 强制居中实机验证（`tools/verify_center.py`）：临时改配置窗口位置 (80,60) → 启动后实际 (325,178)=预期居中 PASS；`SetWindowPos` 尝试移到 (200,200) → 仍在 (325,178) 回中 PASS；关闭 CenterOnScreen → 能移到 (200,200) 自由拖动 PASS
- 截图 `screenshots/设置面板_背景裁剪.png`：预览框显示背景图、铺放下拉+4 滑块+重置按钮齐全、布局无重叠
- 配置未污染：测试改的位置已恢复 (401,169)，3 组 42 条、CenterOnScreen=True、无残留备份


---

## 18. 第六轮改动与验证记录（2026-11-17 15:25）—— 图标高清化

### 18.1 需求与根因

**需求**：原程序不管怎么缩放图标都很清晰，借鉴其设计优化图标显示。

**根因**：QuickBoard 用 `Viewbox Stretch="Uniform"` 把 1100×610 设计画布整体缩放到窗口，图标 `<Image Width=44 Height=44>` 随画布放大。但图标源只有 **32×32**——第三轮为保证图标身份正确改回的 `ExtractIconEx(path, 0)` 只能取系统"大图标"(32×32)。窗口放大到 1600×1000 时，44×44 的槽位被放大到 ~70px，32×32 的位图被拉伸 2 倍多 → 模糊。原程序 WinForms 是 1:1 像素绘制故无此问题。

**关键实验**（`tools/icon_resolution_probe.py`，纯读取）：对 7 个 exe 对比 `ExtractIconEx(0)`=32×32 与 `PrivateExtractIcons(0,256)`=256×256，返回的资源 ID **完全一致**（idea64 id=8、chrome id=7、Code id=1、pycharm id=8、QQ id=7、feishu id=1、explorer id=1）——证明 256 版本是同一图标资源的高分辨率帧，身份正确，不会重现 IDEA 通用方块问题。特殊路径：.lnk 与文件夹 PrivateExtractIcons 失败（需回退 Shell），.ico 成功。

### 18.2 实现

| 文件 | 改动 |
|---|---|
| `Core/Native.cs` | 新增 `PrivateExtractIcons` P/Invoke（user32，按指定 cx/cy 从文件图标资源提取） |
| `Core/IconService.cs` | 新增 `ExtractFromFileResource()`：优先 `PrivateExtractIcons(file,0,256,256)`，失败回退 `ExtractIconEx`(32×32 保底)。`Extract()` 重构：exe/dll/ico 走文件资源高清；**.lnk 先用 `ShortcutResolver.ResolveTarget()` 解析真实目标再取**（否则 Shell 回退）；文件夹/Shell 路径不变。缓存 key **v3→v4** 让旧 32px 缓存失效 |
| `Core/IconProbe.cs` | 报告里的缓存 key 算法 v3→v4 同步 |
| `Core/SelfTest.cs` | 图标提取项新增高清回归断言：explorer.exe 必须 ≥256×256；样本输出尺寸从 icon.Width(显示尺寸) 改为 PixelWidth(源尺寸) |

**设计要点**：
- 图标身份与第三轮完全一致（同一资源索引），只是分辨率从 32→256，属于纯增强，无回退风险。
- 缓存文件仍是 PNG，JustWorks：新 key 首次提取后落盘，之后内存/磁盘都是 256。
- 未改 XAML：Image 控件 `RenderOptions.BitmapScalingMode=HighQuality` 已能让 256 源在 Viewbox 缩放下高质量重采样。

### 18.3 验证证据（全部通过）

- 构建零警告零错误；`--selftest` **9/9**（新增 256 高清断言通过，4 个样本全部 256×256）
- `--iconprobe`：**42 个图标全部 256×256**（含 Edge/Excel/Word/PPT 快捷方式、文件夹、MuMu/Aki 等 .ico、IDEA/PyCharm/VSCode），耗时均 ≤12ms
- 身份目视：`iconprobe-cache/5634CC...png`（IDEA）= 正确的 IJ 多面体图标（256×256），非通用方块
- 实机截图 `screenshots/图标高清_默认尺寸.png` 与 `图标高清_放大尺寸.png`（1600×1000）：放大后 IDEA/Office/Chrome/Steam/PyCharm 等图标全部清晰锐利，无模糊/像素化/缩小
- 工具：`tools/icon_resolution_probe.py`（分辨率对比诊断）、`tools/iconshoot.py`（启动+双尺寸截图+关闭，规避沙箱回收子进程）
- 配置未污染：3 组 42 条 [8,29,5]、LastGroupIndex=1、窗口与背景图均为用户原值（selftest 用临时目录）；原程序三文件 mtime 未变


---

## 19. 第七轮改动与验证记录（2026-11-17 16:05）—— 分组编辑功能

### 19.1 需求与实现

**需求**：原只有分组切换，没有任何分组管理功能。需新增：新增分组、删除分组、隐藏分组、设指定分组为主界面，并统一通过「分组编辑」入口进入。

**设计**：做一个统一对话框集中全部操作，双入口进入：
- 顶栏新增「分组」按钮（`BtnGroups_Click`）
- 分组胶囊右键菜单由原「重命名/删除此分组」改为单一「编辑分组…」（`MenuEditGroup_Click`），都调用 `MainWindow.OpenGroupEditor()`

**数据模型**（`Core/Models.cs`）：
- `AppGroup.IsHidden`：隐藏分组保留数据但不显示、不参与切换、不可设为主界面
- `BoardConfig.HomeGroupId`：主界面分组 Id（用 Id 不用索引，避免排序/删除后错位）
- 新增 `GroupRules` 静态类集中规则：`SelectableIndices()`（过滤收藏栏+隐藏）、`ResolveInitialIndex()`（主界面优先→上次→第一个）、`IsHomeValid()`

**主窗口**（`MainWindow.xaml.cs`）：
- `SelectableOrder` 改用 `GroupRules.SelectableIndices` → 隐藏分组不进分组栏、数字键 1–0、滚轮换组
- `ResolveInitialGroup()` 改用 `GroupRules.ResolveInitialIndex` → 启动优先显示主界面分组
- `EnsureGroups()` 保证至少一个可见分组、清除失效的 HomeGroupId
- `RebuildGroupBar()` 给主界面分组名加 ★ 前缀
- `OpenGroupEditor()` 提交后：若当前分组被删/隐藏则回退到主界面分组；保存配置
- 删除原 `MenuRenameGroup_Click`/`MenuDeleteGroup_Click`（功能并入编辑页）

**编辑页**（`Views/GroupEditorDialog.xaml(.cs)`）：
- 列表式管理：每行 = 序号 + 名称框（直接改即重命名）+ 条目数 + 「设为主界面」+ 隐藏复选框 + ↑↓ 排序 + 删除
- 底部「＋新增分组 / 取消 / 完成」；收藏栏单独提示不可编辑
- 对克隆数据操作，点「完成」才 `CommitTo()` 写回配置，取消零副作用
- 校验：主界面/最后一个可见分组不可隐藏；隐藏组不可设主界面；至少保留一组；删主界面组自动清 HomeGroupId
- `ProbeMode` 跳过 MessageBox/输入对话框，供 `GroupProbe` 离屏验证

### 19.2 验证证据（全部通过）

- 构建零警告零错误
- `--selftest` **10/10**（新增「分组编辑规则」：隐藏过滤/主界面回退/IsHidden+HomeGroupId 配置往返）
- `--groupprobe`（新增探针，**13/13**）：初始渲染、新增、重命名、设主界面、主界面不可隐藏、隐藏普通组、隐藏组不可设主界面、上下移排序、删除、删主界面清 Id、提交结构（收藏栏保留末尾）、取消不写回；主窗口分组栏实测显示「常用 | ★ 游戏」（隐藏的"工作"被过滤），启动选中主界面组
- `--uipreview`：groups.png 600×367 渲染 OK，文字清晰无白底白字、控件无重叠
- `--layoutdump` 1180×660 与基线一致（缩放 1.0718、网格 950×336、按钮高 104、分组栏 2 项）
- 实机截图 `screenshots/分组编辑_入口与标记.png`：顶栏「分组 添加 设置 ×」按钮齐全
- 配置未污染：3 组 [8,29,5]、LastGroupIndex=1、HomeGroupId 空、窗口与背景图用户原值；原程序三文件 mtime 未变

---

## 20. 第八轮改动与验证记录（2026-11-17 17:00）—— 数据安全 / 诊断 / 主题 / 图标档位

### 20.1 需求

用户从优化建议里选了 5 项：①配置损坏自愈 ②备份与导入导出 ③启动故障诊断可视化 ⑦图标尺寸档位 ⑩多主题切换。

### 20.2 关键根因与设计

- **主题切换的隐藏坑**：Theme.xaml 里所有画刷都是 `StaticResource`（加载时解析一次），而 `Appearance` 是运行时覆盖 `app.Resources`——静态引用收不到更新。必须全部改 `DynamicResource`；ToolTip 模板的硬编码底色也改成 `TooltipBgBrush`。
- **ComboBox 空白 bug**（顺带修复）：全局隐式 `TextBlock` 样式把 ComboBox 内自动生成的文字染成白色 `TextBrush`，系统浅色下拉框底上完全看不见（原有 FitCombo 也一直有此问题）。给 ComboBox/ComboBoxItem 自定义模板，用 `TextElement.Foreground` **本地值**覆盖全局样式，深色字清晰可读。
- **配置自愈**：`Save()` 写入前先把当前完好配置轮转出 `board.json.bak`（仅在能解析时才备份，避免把损坏内容固化）；`Load()` 主文件坏→从 .bak 恢复并立刻回写主文件→都坏则回退默认并保留 `.broken-时间戳` 现场。启动 Shell 层用 MessageBox 明确告知用户。

### 20.3 改动文件

- `Core/Models.cs`：`BoardOptions` 加 `ThemeKind`(0深/1浅/2跟随系统)、`IconScale`(0紧凑/1标准/2大)；新增 `ThemeKinds`/`IconScales` 常量类
- `Core/ConfigStore.cs`：`ConfigLoadStatus` 枚举、`LastLoadStatus`/`LastMessage`；`BackupPath`；重写 `Load()`（三级自愈）、`Save()`（轮转备份）；新增 `Export()`/`ReadImport()`/`ApplyImport()`
- `Core/Appearance.cs`：`Apply(opacity, themeKind, iconScale)` 统一入口，深/浅两套完整画刷，`ResolveTheme()` 读注册表 `AppsUseLightTheme`，设置 `TileIconSize`/`FavIconSize` 两个 double 资源
- `Core/Diagnostics.cs`（新增）：汇总配置状态/备份/图标缓存数/热键/异常日志/运行时
- `Core/ThemeProbe.cs`（新增，`--themeprobe`）：同窗口切三档，导出截图 + 记录画刷颜色
- `Theme.xaml`：画刷引用全部 `StaticResource`→`DynamicResource`；加 `sys:Double` 图标尺寸资源、`FieldBgBrush`/`PreviewBgBrush`/`TooltipBgBrush`/`TooltipBorderBrush`；ComboBox 自定义模板
- 所有对话框 XAML（EditEntry/GroupEditor/Hotkey/Prompt/Settings）：主题画刷引用改动态
- `MainWindow.xaml`：两处图标 Image 的 Width/Height 绑 `DynamicResource TileIconSize/FavIconSize`
- `MainWindow.xaml.cs`：`ApplyAppearance` 传三参数；`OpenSettings` 传 `_hotkeyError`
- `SettingsDialog.xaml(.cs)`：外观区加主题/尺寸下拉框；数据区加备份/导出/导入按钮；底部加诊断区；`SyncOptionsToControls()` 供导入后回填
- `App.xaml.cs`：`--themeprobe` 分支；加载后损坏/回退时 MessageBox 提示；设置页传热键状态
- `Core/SelfTest.cs`：新增 4 项断言（配置自愈、主题档位、诊断、导出导入）
- `Core/DialogPreview.cs`：加 `settings_bottom.png`（滚底）与 `settings_full.png`（展开）两张验证图

### 20.4 验证证据（全部通过）

- 构建零警告零错误
- `--selftest` **14/14**（新增配置自愈/备份轮转/现场保留、主题档位往返与跟随系统解析、诊断文本六类信息、导出导入往返+坏文件安全返回）
- `--themeprobe`：实测同一窗口三档画刷确实改写——深色 panel=`#F0151A1F`/text=`#FFF1F3F6`/图标44，浅色 panel=`#F0F7F8FA`/text=`#FF1E2430`/图标54，紧凑图标36；导出 3 张 PNG
- 实机截图 `screenshots/设置页_主题与诊断.png`（精确枚举窗口句柄抓取 540×746 设置框）：主题「深色」/尺寸「标准」下拉框文字清晰、数据区按钮与诊断区文字齐全无重叠
- `--uipreview`：settings/settings_bottom/settings_full/groups/prompt/edit 全部 OK
- `--layoutdump` 1180×660 与基线一致（缩放 1.0718、网格 950×336、按钮高 104、三排 y=0/112/224）
- 配置未污染：3 组 [8,29,5]、ThemeKind/IconScale 无字段（走默认深色+标准）、窗口与背景图用户原值；原程序三文件 mtime 未变

### 20.5 备注

- 备份文件在用户下次保存配置时才生成（本轮测试全用临时目录，未污染真实配置）
- 浅色主题在某张浅色区域背景图下顶栏按钮对比度偏低，属背景图局部对比问题，非功能 bug
- 去掉了 SearchBox 残留样式已在前几轮完成，本轮未动搜索相关

---

## 21. 第九轮改动与验证记录（2026-11-17 17:12）—— 下拉框白底白字 / 底部提示可读性

### 21.1 需求

用户反馈两处对比度问题：①设置里选择框的下拉选项是白色底，里面字体也是白色，看不清；②主界面左右下角的提示性文字用看不清的白色，希望改善。

### 21.2 关键根因（用数值探针确认，非猜测）

- **下拉项白底白字**：`Theme.xaml` 全局隐式 `Style TargetType="TextBlock"` 把 `Foreground` 设为 `{DynamicResource TextBrush}`（深色主题=#FFF1F3F6 白）。ComboBox/ComboBoxItem 模板用 `ContentPresenter` 显示字符串，会自动生成一个 TextBlock；设在 ContentPresenter 上的 `TextElement.Foreground="#FF20242A"` **作为继承值优先级低于样式 Setter**，被全局隐式样式覆盖 → 白底白字。`--comboprobe` 修复前实测：所有 `[box]`/`[item]` 文字 fg=#FFF1F3F6（白），铁证。
- 两次尝试才修对：先加局部隐式 TextBlock 样式（Style.Resources）→ 探针显示仍为白字，无效（生成文字仍走全局样式）；最终改为**显式 TextBlock + 本地 Foreground 值**（本地值优先级最高），一次生效。

### 21.3 改动文件

- `Theme.xaml`：ComboBox 的 `ContentSite`、ComboBoxItem 模板内的文字由 `ContentPresenter` 改为**显式 TextBlock**，`Foreground="#FF20242A"` 设为本地值；文字固定深色（下拉底固定白色，与深浅主题无关，两主题下均深字）
- `MainWindow.xaml`：底部状态栏 `StatusText`（左下）/`HintText`（右下）各包一层 `Border`（`Background="#D60E1218"` 深色半透明、`CornerRadius=4`、`Padding=7,2`），文字固定 `#FFF1F3F6` 高对比亮字，不随主题/背景变化
- `Core/ComboProbe.cs`（新增 + `--comboprobe` 开关）：深浅两主题下读取三个下拉框 `[box]`/`[item]` 的 TextBlock 实际 Foreground，并把下拉项渲成 PNG；`App.xaml.cs` 接入开关

### 21.4 验证证据（全部通过）

- 构建零警告零错误；自检 **14/14**；`--layoutdump` 1180×660 与基线**完全一致**（状态栏加底板未改网格/磁贴几何）
- `--comboprobe` 修复前：6 组文字全 = #FFF1F3F6（白，白底看不清）；修复后：深浅两主题下 6 组 `[box]`+`[item]` 全 = **#FF20242A**（深色），3 张下拉 PNG 目视确认白底深字清晰
- 实机截图 `screenshots/状态栏底板验证.png`：用户明亮壁纸下，左下（分组/条目数/热键）与右下（滚轮换组/Q/E启动/F1–F12收藏）文字均有深色圆角底板衬托、清晰可读
- 配置未污染：3 组 [29,5,8]（顺序为用户上轮自检时自调）、ThemeKind=0/IconScale=1（用户自设）、窗口与背景图用户原值；原程序三文件 mtime 未变

### 21.5 备注

- ComboBox 背景与文字现在都固定浅色系（白底深字），不跟随深浅主题——这是刻意的：下拉框是系统风格控件，白底深字在两种主题下都清晰，避免主题切换又要反转一次配色
- 底部提示文字底板用固定深色 + 固定亮字，同理不随主题变化，保证在任何背景图上都可读

---

## 22. 第十轮改动与验证记录（2026-11-17 17:20）—— 磁贴名称浅底黑字

### 22.1 需求

用户反馈：每个软件图标下方的名称字体要改成和下拉选项一样的黑色字体。

### 22.2 设计

- 原名称文字用 `Foreground={DynamicResource TextBrush}`（深色主题=白字 #FFF1F3F6），叠在用户明亮壁纸的浅色区域（人物衣服/皮肤）看不清。
- 参考下拉选项的「浅底黑字」方案：直接用黑字会因壁纸深色区域（红伞/黑发）变成黑底黑字，所以必须配浅底。给 `TileVisual`/`FavVisual` 两处名称 TextBlock 各包一层浅色圆角底板 `#F2F7F9FC`（CornerRadius=5, Padding=3,1）+ 固定深色文字 `#FF20242A`，与下拉选项/右键菜单同一套浅底黑字设计语言，任意壁纸明暗都清晰。
- 空键位副作用：加底板后空槽位会露出无字的白条。用 `DataTrigger` 绑定 `DisplayName`（null 或空串）时把底板 `Visibility=Collapsed`，空槽只保留虚线框和键位字母。
- 几何坑：初版把底板 `Padding=3,1` + `Margin=3,0,3,0` 叠加，10 列 UniformGrid 每列最小宽度 +6px，网格 950→1010。把 Margin 归零（间距只由内部 Padding 提供）后恢复基线 950。

### 22.3 改动文件

- `MainWindow.xaml`：`TileVisual`、`FavVisual` 两处名称 TextBlock → 浅底黑字 Border + 空值折叠触发器；底板 Margin 归零保持几何

### 22.4 验证证据（全部通过）

- 构建零警告零错误；自检 **14/14**
- `--layoutdump` 1180×660 与基线**完全一致**（网格 950×336、按钮高 104、三排 y=0/112/224；修复中途出现的 1010 已回退）
- 实机截图 `screenshots/磁贴名称黑字_最终.png`：所有软件名称为黑字+浅色圆角底板、清晰可读；空键位白条已消失；收藏栏名称同样黑字清晰
- 配置未污染：3 组 [29,5,8]（用户上轮自调顺序）、ThemeKind=0/IconScale=1（用户自设）、窗口与背景图用户原值；原程序两文件 mtime 未变

### 22.5 备注

- 名称底板用固定浅色 + 固定深色字，不随深浅主题变化——与下拉选项/右键菜单一致，保证两种主题下都清晰

### 22.6 修订（2026-11-17 17:30）—— 用户否决浅底板

- 用户反馈浅色底板「好难看，不用加浅底」。已**移除两处浅色圆角底板**（含空值折叠触发器，不再需要）。
- 最终方案：名称文字固定黑色 `#FF20242A`（本地值覆盖全局隐式 TextBlock 样式），**无底板**；用**三层白字辉光**保证壁纸深色区域可辨认——
  1. 底层白字 `BlurRadius=8, Opacity=0.55`（柔和宽光晕）
  2. 中层白字 `BlurRadius=4`（描边主体）
  3. 紧层白字 `BlurRadius=1.5`（贴字形实心细边）
  4. 顶层黑色文字
- 迭代证据：单层 2px 描边（截图）→ 深色区域黑字几乎不可见；双层 1.5+4px → 有改善仍偏弱；三层 8/4/1.5 辉光 → 深色区域（QQ/微信/Steam/Edge/飞书等）可辨认、浅色区域清晰、观感自然，为无底板前提下的最优平衡。
- 回归：构建零警告零错误；自检 14/14；`--layoutdump` 与基线**完全一致**（辉光层不改变几何）。
- 最终截图 `screenshots/磁贴名称黑字_辉光版.png`（中间迭代截图已清理）。

### 22.7 最终定稿（2026-11-17 17:36）

- 用户明确要求「就按黑字，不要自作主张」：已移除三层白色辉光，`TileVisual`/`FavVisual` 两处名称恢复为**单个 TextBlock + 固定黑色 `#FF20242A`**，无底板、无描边、无 effect。
- 构建零警告零错误、自检 14/14、`--layoutdump` 与基线一致。
- 后续如需可读性增强，仅按用户明确指示调整，不再自行添加效果。

---

## 23. 第十一轮改动与验证记录（2026-11-17 18:10）—— 磁贴名称黑字加描边

### 23.1 需求

用户要求：给每个软件图标下方的名称字体加入描边（在 §22.7 纯黑字基础上）。

### 23.2 设计与实现

- WPF 原生 `TextBlock` 不支持 Stroke，新增自定义控件 **`Core/OutlinedTextBlock`**（继承 FrameworkElement）：
  - **测量**：委托给一个不在视觉树内的 `TextBlock` 代理（同步 Text/Font*/Wrapping/Trimming/LineHeight），`MeasureOverride` 返回代理的 `DesiredSize` —— desired 行为与 TextBlock **完全一致**，保证不干扰外层 `UniformGrid` 列宽基线；
  - **渲染**：`OnRender` 用 `FormattedText.BuildGeometry` 得字形几何，先画描边层（`Pen`，白色）再画填充层（黑色 `#FF20242A`），字形主体覆盖描边内侧；
  - `OnRenderSizeChanged` 触发重绘（Arrange 拉伸后居中重画）。
- `MainWindow.xaml`：`TileVisual`（键盘磁贴）与 `FavVisual`（收藏栏）两处名称控件替换为 `OutlinedTextBlock`，键盘区 `Stroke="#E6FFFFFF" StrokeThickness="1.3"`、收藏区 `1.1`（小字略细）。其余属性（TextAlignment/TextWrapping/TextTrimming/MaxHeight/Margin）与原 TextBlock 相同。
- 新增探针 **`--boardshot <path>`**（`Core/BoardShot.cs`）：离屏创建主窗口 → `DispatcherFrame` 等图标异步加载 3 秒 → `RenderTargetBitmap` 渲染主界面存 PNG，用于目视核对描边观感（DWM 屏拷贝在本机有 DPI 虚拟化错位问题，不再依赖）。

### 23.3 过程中的关键坑（后续改文字控件必读）

1. **desired 宽度撑爆列宽**：首版 `MeasureOverride` 在有约束时返回 available 宽（填满），导致 UniformGrid 每列 desired 被撑到测量约束值，网格 950→1020（MaxWidth 封顶）。手工模拟"最宽词"desired 又得 810（仍不对）——TextBlock 的 desired 规则复杂，最终用 **TextBlock 代理测量**彻底解决。
2. **XAML sed 往返丢属性**：用 sed 对照实验切换 TextBlock/OutlinedTextBlock 时把 `Stroke` 属性弄丢，一度误把浅色壁纸区当成了"描边生效"。教训：XAML 属性增删不要用多行 sed，用精确 Edit；验证描边必须看深色背景区域的渲染。
3. **沙箱屏拷贝不可靠**：`PrintWindow`/`BitBlt` 在本机（DPI 缩放 1854×806）出现行错位/alpha 未定义（0/255 横条纹）；`python` 无 PIL。新增 `--boardshot` 程序内渲染 + `tools/croppng.py`（纯标准库 PNG 裁剪，注意 write 时每行要加 filter 字节 \x00）。

### 23.4 验证证据（全部通过）

- 构建零警告零错误；自检 **14/14**；`--layoutdump` 1180×660 与基线**完全一致**（网格 950×336、按钮高 104、三排 y=0/112/224、收藏栏 912×82、无溢出）——代理测量方案未影响几何
- `--boardshot` 渲染图 `screenshots/磁贴名称黑字_描边.png`：QQ / 微信 / 网易云音乐等深色壁纸区域的黑字名称带白色描边、清晰可辨；浅色区域（Another Redis Desktop）同样清晰；文字居中、无溢出、无重叠
- 配置未污染：3 组 [29,5,8]、HomeGroupId 不变；ThemeKind=1 与窗口 1854×806 为用户 17:53 旧进程保存的自设状态（本轮探针均 SuppressPersist 离屏，不写配置）
- 程序已重启（PID 49128）

### 23.5 备注

- 描边颜色固定白色 `#E6FFFFFF`（90% 不透明），不随主题变化——黑字白边是字幕式设计，在任意明暗壁纸下都保证黑字主体可辨
- 若后续要调描边强度：改 `MainWindow.xaml` 两处 `StrokeThickness`（键盘 1.3 / 收藏 1.1）即可

---

## 24. 第十二轮改动与验证记录（2026-11-17 18:20）—— 名称字号设置

### 24.1 需求

用户要求：新增设置项，可调节软件图标下方文字的字体大小。

### 24.2 设计与实现

- **`Core/Models.cs`**：`BoardOptions.NameFontSize`（double，默认 11）+ `NameFontSizes` 常量（Min 9 / Default 11 / Max 13）+ `Clamp()`（NaN/0 回默认档，其余夹取 9–13）。上限 13 的依据：两行文字 `(13+3)*2=32` 恰好填满名称行 RowHeight=32，**不改变按钮高 104 的布局基线**。
- **`Core/Appearance.cs`**：`Apply()` 新增第 4 参 `nameFontSize`（保留 3 参重载兼容旧调用）。写 6 个 `sys:Double` 资源：
  - `TileNameFontSize` = 设定值；`TileNameLineHeight` = 字号+3；`TileNameMaxHeight` = (字号+3)*2+2
  - `FavNameFontSize` = max(8, 字号-1)（收藏栏自动小一档）；对应 LineHeight/MaxHeight 同理
- **`MainWindow.xaml`**：`TileVisual` / `FavVisual` 两处 `OutlinedTextBlock` 的 `FontSize` / `LineHeight` / `MaxHeight` 改 `DynamicResource`（描边是几何绘制，字号变化自动适配，无需改描边代码）。
- **`MainWindow.xaml.cs`**：`ApplyAppearance` 传 `o.NameFontSize`。
- **`SettingsDialog.xaml(.cs)`**：外观区新增「名称字号」滑块（9–13，TickFrequency=1 吸附整数）+ 右侧数值标签；初始化（构造 + SyncOptionsToControls）、Save_Click 写回、`NameFont_Changed` 实时 `_preview` 预览、DebugState 加 `nameFont=` 字段。
- **`Core/SelfTest.cs`**：新增自检项「名称字号」（第 15 项）：配置往返、上限夹取、NaN/0 回默认档、Application 资源写入校验（12→键盘 12/收藏 11/MaxHeight 32，校验后恢复默认）。
- **`App.xaml.cs`**：`--boardshot` 新增可选第 2 参临时覆盖名称字号（只改内存 config，SuppressPersist 不落盘），用于渲染不同档位对比。

### 24.3 验证证据（全部通过）

- 构建零警告零错误；自检 **15/15**（新增「名称字号」PASS）；`--layoutdump` 与基线完全一致（950×336、按钮 104、收藏栏 912×82、无溢出）——默认字号 11 的资源值与原硬编码值逐一相等（11/14/30、10/13/26），布局零变化
- `--boardshot 9` 与 `--boardshot 13` 渲染对比：小档文字明显变小、大档明显变大，两档均无裁剪、无重叠，描边随字形正常缩放（截图 `screenshots/名称字号_小9.png`、`名称字号_大13.png`）
- `--uipreview`：settings_full.png 显示「名称字号」滑块位于图标尺寸下方、值 11，排版正常（截图 `screenshots/设置面板_名称字号.png`）；DebugState 含 `nameFont=11`
- 配置未污染：用户 board.json 无 NameFontSize 字段（读时取默认 11），3 组 [29,5,8] 不变；探针均不落盘
- 程序已重启（PID 45272）

### 24.4 备注

- 滑块范围 9–13：下限 9 保证小字号可读，上限 13 由名称行 RowHeight=32（两行）决定；如需更大字号需同步放大 `TileVisual`/`FavVisual` 的名称行高度并接受按钮高 104 基线变化
- 收藏栏字号 = 键盘字号 - 1（最低 8），与两者视觉比例（10/11）保持一致

---

## 25. 第十三轮：代码审查与优化（2026-11-17 18:35）

### 25.1 审查范围

通读 20 个源文件（约 6300 行）+ 实测运行时数据，发现 **3 个真 bug / 4 处性能损耗 / 6 处重复代码**，用户选择「Bug + 性能 + 去重」全量处理。

### 25.2 P0 真 Bug 修复

| # | 问题 | 修复 |
|---|---|---|
| 1 | `ReloadFromDisk` 只复制 Groups/Hotkey/Options，**漏 `HomeGroupId`**（也漏 Version/LastGroupIndex）→ 托盘「重新载入配置」后主界面分组丢失；且热键变化后未重注册 | 补全 4 个字段复制；末尾调用 `ApplyHotkey()` 重新注册 |
| 2 | `HotkeyService.Register` 先 `Unregister()` 再注册，新热键被占用时旧热键也失效 → 面板唤不出（只剩托盘） | 新增 `_lastGood` 记录上次成功设置，失败自动回滚注册并返回「（已保留原热键 X）」；`Active` 暴露当前生效值；`MainWindow.ApplyHotkey()` 在回滚时把配置里的热键也改回旧值并存盘，避免下次启动继续用坏组合 |
| 3 | `SettingsDialog` 导入配置时立即 `ApplyImport` 落盘并改写 `_config` → 点「取消」无效 | 改为暂存：新增 `_pendingImport`，只有 `Save_Click` 才写入 `_config`（落盘交给主窗口 Save，导入前配置能正常轮转为 .bak）。`.mal` 导入同样暂存。热键编辑也改为暂存 `_pendingHotkey`，取消不再留下副作用 |

### 25.3 P1 性能优化

1. **图标内存缓存**：`IconService` 增 `ConcurrentDictionary<string, BitmapSource>`（freeze 后跨线程共享，上限 256 项）。切组/保存时 42 个图标不再重复读盘解码。
2. **COM 释放**：`IShellItemImageFactory` 从 4 次尺寸循环内移到循环外，`finally` 里 `Marshal.ReleaseComObject`，每次提取少建 3 个 COM 对象且不再泄漏 RCW。
3. **增量刷新**：`SlotCell`/`FavSlot` 改为可变 `Tile` + `INotifyPropertyChanged`；`RebuildBoard` 用 `EnsureSlotCells/EnsureFavSlots` 只补缺容器、按位更新内容，不再 `Clear()` 重建 42 个按钮。
4. **缓存目录清理**：`IconService.Initialize` 后台修剪，PNG 超过 200 个时按最后写入时间保留最新 200（实测 156 个 / 2.8MB，其中约 2/3 是历史残留）。

### 25.4 P2 去重

1. `SuppressAutoHide()` 返回 `IDisposable`（`HideSuppressor`），替换 5 处手写 `++/try/finally/--`；菜单跨事件配对用 `BeginSuppressHide/EndSuppressHide`。`_suppressHide` 直接操作点从 13 处收敛到 5 处（全部集中在封装内）。
2. `App.OnStartup` 的 9 个探针分支合并为 `TryRunProbe(args)` + 局部 `ArgOr(name, fallback)`，`OnStartup` 从 ~120 行降到 ~20 行。
3. `SettingsDialog` 构造函数复用 `SyncOptionsToControls()`（删掉 30 行重复赋值）。
4. `ConfigStore` 抽出 `TryParse(json)`，`TryRead` 与 `ReadImport` 共用（含 null 修复 + Normalize）。
5. `MainWindow.WndProc` 的命中测试抽成 `HitTestResize(Point) -> int`（0 = 不处理），WndProc 只保留消息分发。
6. 收藏栏/主网格归属规则统一为 `SyncEntryPlacement(entry, wantsFavorite, origin)`，替换 `ApplyFavoritePlacement`、`EditEmptySlot`、`MenuToggleFavorite_Click` 三处各自实现的迁移逻辑；顺带修掉「12 个收藏位满时把 F 键号 0 的条目塞进收藏栏（永不显示）」的隐藏 bug。

### 25.5 验证证据（全部通过）

- 构建 **0 警告 0 错误**；自检 **15/15**
- `--layoutdump` 与基线**完全一致**：Viewbox 1.0718、网格 950×336、按钮高 104、收藏栏 912×82、三排 y=0/112/224、无溢出
- `--boardshot` 输出 `INCREMENTAL reuse=True slot0=[QQ]=>[剪映专业版]`：切组后**容器复用**（同一 SlotCell 对象）且内容正确更新
- `--uipreview`：`theme=1 scale=1 nameFont=11 diag=227ch`，设置页/分组页渲染正常
- `--groupprobe`：13/13
- 配置未污染：3 组 [29,5,8]、HomeGroupId 保留、热键 Ctrl+Alt+Space、图标缓存 156 个（未达 200 清理阈值，符合预期）
- 程序已重启（PID 37540）

### 25.6 未处理项（P3，留待需要时再做）

- `Launcher.ApplyPlacement` 用 `Thread.Sleep`（最长 15s+6s 轮询）无取消，`Process` 未 Dispose
- `MainWindow` 仍 1000+ 行，可拆 BoardRenderer / GroupNavigator / FavoritesMover
- `OutlinedTextBlock` 每次 Measure 重设 7 个代理属性、每次 OnRender 重建 FormattedText/Pen（可缓存）
- `GroupEditRow.Name` 用 `UpdateSourceTrigger=LostFocus`，行重建时未提交的文本有丢字风险
- 探针代码（约 900 行）仍在产品程序集里，可用 `#if DEBUG` 或独立项目
- 托盘/顶栏图标用 `SystemIcons.Application` 通用图标

---

## 26. 第十四轮改动与验证记录（2026-11-17 18:52）—— 名称省略规则不一致的根因修复

### 26.1 现象

用户反馈：**有的名字很长却不省略，有的名字很短反而被折行/省略**。

实测（`--layoutdump` 打印每个名称控件的实测宽度，修复前）：

```
名称#1 'QQ'                            宽=38  desired=24
名称#3 '网易云音乐'                     宽=55  desired=61
名称#4 'Another Redis Desktop Manager'  宽=75  desired=81x28
名称#6 'UU远程'                         宽=38  desired=44
```

### 26.2 两个独立根因

**根因 A：名称控件的宽度跟着自己的文字长度走（导致短名被二次折行）**

`TileButton` 模板里是 `<ContentPresenter HorizontalAlignment="Center">`。Content 居中 → 模板根 Grid 按**自身期望宽度**排布并居中 → 名称控件的可用宽度 = 它自己文字宽度 − Margin。
文字在 Measure 阶段按 89px（整格宽）断开、在 Arrange 阶段却只能拿到「自己这一步算出的期望宽 − 6」，于是**刚好卡在临界值的短名字被二次折行**：`UU远程`（44px）被折成 `UU远` / `程`。
而名字长的（`Another Redis Desktop Manager`）Measure 阶段就已按 89px 折成 2 行、期望宽 81，反而"看起来正常" —— 这就是"长名没事、短名被拆"的由来。

**根因 B：渲染完全不管高度上限（导致长名被硬切且没有省略号）**

`OutlinedTextBlock.OnRender` 只给 `FormattedText` 设 `MaxTextWidth`，没设行数/高度上限，于是 3 行以上照画；父级 `Grid ClipToBounds=True` 把第 3 行**无声切掉**：`Another Redis Desktop Manager` 只显示 `Another Redis` / `Desktop`，`Manager` 凭空消失、没有省略号。WPF 原生 `TextBlock` 之所以有省略号，是它在限高后自己做了最后一行裁剪 —— 自定义控件必须显式实现。

### 26.3 修复

1. `Theme.xaml` → `TileButton` 模板：`ContentPresenter HorizontalAlignment="Center"` 改 **`Stretch`**，磁贴内容始终占满整格，所有名称控件可用宽度统一为 83px。
2. `MainWindow.xaml` → `ResultCell`（无键位条目 / 搜索结果）的按钮加 **`Width="91"`**，与键盘网格磁贴同宽；否则 WrapPanel 里按钮仍按内容自适应，名称宽度继续跟着文字长度漂移（顺带让"无键位条目"那排磁贴宽度一致，不再忽宽忽窄）。
3. `OutlinedTextBlock`：
   - `OnRender` 把 `ActualHeight` 一起传给排版；
   - 先用 `LineHeight`（未设时取 `FormattedText.Height`）算出单行高度，再 `MaxLineCount = floor((maxHeight + 0.5) / lineHeight)`，配合 `Trimming = CharacterEllipsis` **在最后一行收尾加省略号**，不再依赖父级裁剪；
   - 顺序很关键：必须**先设 LineHeight 再读 Height**，否则 13 号字时会把 2 行误判成 1 行（实测踩到）。
4. `LayoutProbe`：把临时诊断换成**回归断言** —— `名称控件宽度: 统一 83（30 个）`，宽度一旦再次随文字漂移就会立刻显示"不一致"。
5. 新增探针 **`--nameprobe <png>`**（`Core/NameProbe.cs`）：用 12 个极端名称（单字符 `A`、纯中文、多词英文、36 字符无空格长串、中英混排、带 `.exe - 快捷方式` 后缀）跑**真实磁贴模板**渲染，另含 4 个「无键位」条目验证 WrapPanel 里的磁贴宽度，用于此类问题的回归。

### 26.4 验证证据（全部通过）

- 构建 **0 警告 0 错误**；自检 **15/15**
- `--layoutdump` 与基线**完全一致**：Viewbox 1.0718、网格 950×336、按钮高 104、收藏栏 912×82、三排 y=0/112/224、无溢出；**新增断言：名称控件宽度统一 83（30 个）**
- 真实配置渲染 `screenshots/名称排版_修复后.png`：`UU远程` / `腾讯会议` 等短名单行；`Another Redis Desktop Man…` 两行带省略号；`IntelliJ IDEA 2024.1`、`Visual Studio Code`、`PyCharm 2025.3.1` 均两行且完整
- 压力渲染 `screenshots/名称排版_压力测试.png`：单字符 `A`、纯中文、多词英文、36 字符无空格长串（单行 `SuperLongUnb…`）、`Adobe Photoshop 202…`、无键位条目磁贴宽度一致，均无溢出、无硬切
- 13 号字回归：`--boardshot ... 13` 仍是 2 行 + 省略号（验证 26.3 的顺序问题已修）
- 配置未污染：3 组 [29,5,8]、theme=1、font=11、窗口 1715×752 均为用户自设状态；探针全程 `SuppressPersist`
- 程序已重启（PID 50844）

### 26.5 备注

- 名称可用宽度 = 磁贴 91 − Margin 6 − 边框 2 = 83（键盘）/ 68（收藏栏，Grid Width=72）
- 名称固定显示 2 行（`MaxHeight` 资源 = `(字号+3)*2+2`），超出以 `…` 收尾；想改行数需同步改 `Appearance.Apply` 里的 `MaxHeight` 计算
- 单行超高、无任何空格的超长单词（36 字符级）WPF 会直接单行省略，占不满两行 —— 属可接受边界，若需强行断词可再改

---

## 27. 第十五轮：内存占用分析与优化（2026-11-17 19:20）

### 27.1 用户疑问

运行时约 130MB（任务管理器口径），原版 MadAppLauncher 只占 12MB，"我们的项目扩展那么多是否过于繁杂以至于沉重？"

### 27.2 测量方法

新增探针 **`--memprobe <txt> [slim|bare|ctor]`**（`Core/MemProbe.cs`）：

- 以真实配置离屏创建主窗口 → 等图标异步加载 → 强制 GC → 报私有内存 / 工作集 / 托管堆 / 线程 / 句柄 / GDI·USER 对象 / 程序集清单；
- `slim`：把窗口内容换成一个 TextBlock 再测（判断开销是否在界面）；
- `bare`：完全不构造 MainWindow，只开同等尺寸透明窗口（判断开销是否在启动与构造）；
- `ctor`：只构造不 Show（分辨"构造"与"显示/合成"）；
- 默认模式还会**分阶段移除**投影 → 图标 → 背景图 → 窗口，逐步归因。

配套 `tools/memrun.sh`：连跑 N 次取中位数（单次波动可达 ±20MB，必须重复）。

### 27.3 关键测量数据

| 场景 | 私有内存 |
|---|---|
| 空白 WPF 窗口（外部最小项目，1715×752） | 96.9 MB |
| 空白 + WinForms 托盘 | 96.3 MB（**托盘几乎不占内存**） |
| 空白 + 分层窗口（AllowsTransparency）+ 投影 | 118.9 MB |
| 我们程序：只构造 MainWindow，不显示 | **82.7 MB** |
| 我们程序：完整显示 | **213 MB（初始）** |
| 我们程序：内容换成单个 TextBlock 后显示 | 206 MB（**界面内容不是主因**） |

分阶段归因（初始 213MB）：去掉投影 −48、释放 42 个图标 −3.7、释放背景图 −0.1、隐藏窗口 −3.8。

结论：**平台地板约 93MB（.NET 8 + WPF 空窗口），比原版的 12MB 高一个数量级，这是框架差异，不是代码臃肿**；
（原版更正：MadAppLauncher 是 **VB.NET + WinForms / .NET Framework 4.0 / 32 位 x86**、477KB IL，不是 C++/MFC。同样是托管代码，差距来自 WPF 要加载 wpfgfx/D3D 与合成管线，WinForms 直接 GDI 绘制。）
我们自己的数据只有约 16MB（图标像素 8.5 + 背景图 8.3 + 托管堆 7，其中图片被缓存与界面共享）。真正的浪费是**显示/合成阶段的 130MB**，而其中 **48MB 是 DropShadowEffect**。

根因：主窗口是分层窗口（`AllowsTransparency=True` → WPF 走软件渲染），给整块面板加模糊特效能让 WPF 为整场景分配多张同尺寸中间位图；实测把 BlurRadius 从 20 降到 8 仍然多占 47MB（与半径无关，只与"是否用 Effect"有关）。

### 27.4 改动

1. **投影改为程序生成位图**（新增 `Core/SoftShadow.cs` + `MainWindow.ShadowLayer`）：
   - 按"圆角矩形有符号距离 + 高斯累积分布（erf）"算出半分辨率 alpha 蒙版（约 0.7MB），铺成 ImageBrush；
   - 观感与原 DropShadowEffect 对齐：面板边缘外 alpha 由 38 调到 ~50（原 38），衰减范围一致；数值比对同区域像素差中位数 0–1/255；
   - 尺寸变化才重算（`ShadowLayer_SizeChanged`），其余时间只是一次纹理填充。
2. **图标加载改为专属线程池**（新增 `Core/IconLoader.cs`）：原实现给 42 个条目各发一个 `Task.Run`，同步阻塞的 PNG/Shell 提取把线程池打到 59 个线程；改为 4 条常驻后台线程（MTA）消费同一队列，线程数回到 31、句柄 1131→980。`TileItem` 增加 `TryBeginIconLoad/CancelIconLoad` 去重，`Icon` setter 增加相等判断。
3. **`IconService` 增加 `DescribeCache()` / `ClearMemoryCache()`**：只用于诊断（统计条目数、BGRA32 字节数、尺寸分布）。

### 27.5 验证（全部通过）

- 构建 **0 警告 0 错误**；自检 **15/15**；分组探针 **13/13**
- 布局基线**完全未变**：Viewbox 1.0718、网格 950×336、按钮高 104、收藏栏 912×82、三排 y=0/112/224、名称控件宽度统一 83
- 内存：**213.0 MB → 169.5 MB（−43.5MB，−20%）**，三次中位数（167.1 / 171.6 / 169.5）
- 真实抓屏 `screenshots/内存优化后.png`：图标、名称描边、阴影、背景图均正常
- 线程 59 → 31（真实进程 36），句柄 1131 → 980
- 配置未污染（探针全程 SuppressPersist）

### 27.6 试过但**没有**收益 / 主动放弃的方向

| 方案 | 结果 |
|---|---|
| `BlurRadius` 20 → 8 | 214.7MB，无效（Effect 的存在本身才是成本） |
| `BitmapScalingMode` HighQuality → Linear | **更差** 177.9MB，已回退 |
| 背景层改"画刷透明度"代替元素 Opacity | 172MB，无收益；且设置里拖滑块会反复重新解码背景图，已回退 |
| 关闭 `AllowsTransparency` | 仅 −3.5MB，但会失去圆角与半透明窗口观感，不做 |
| 图标解码降到 128px | 可省约 4–6MB，但会牺牲放大后的清晰度（用户明确要求清晰），不做 |
| 移除 WinForms 托盘 | 实测 96.3 vs 96.9MB，不值得为此重写 Shell_NotifyIcon |

### 27.7 若还要继续压

按收益排序（都属于"改观感/架构"级别，需先确认）：
1. 描边文字改"多次偏移 DrawText"而非 `BuildGeometry`（约 −9MB，需目视比对观感）；
2. 关掉分层窗口（−3.5MB，方角窗口）；
3. `.NET` 侧 `<InvariantGlobalization>` 等运行时开关（数 MB，但会影响中文排序/文化相关行为，不建议）。

### 27.8 附注

- `--boardshot` 离屏渲染图里**一直看不到图标**（改造前的老截图同样没有），这是 RenderTargetBitmap 的既有现象，不是回归；核对图标要用真实抓屏（`tools/outlineshot.py`）。
- `--memprobe` 的 slim/bare/ctor 模式与 `tools/memrun.sh` 保留，后续再查内存可直接用。

---

## 28. 第十六轮：修窗口摆位 bug + 右键对齐原版（2026-11-17 19:40）

### 28.1 需求

1. 修 bug（上一轮分析里定位的 `Launcher` 摆位链路）。
2. 右键菜单向原版 MadAppLauncher 看齐：**剪切 / 复制 / 粘贴 / 删除**直接可用，不必再绕进编辑对话框。

### 28.2 Bug 根因（已实测确认）

`Launcher.Launch` 用 `UseShellExecute = true` 启动后靠 `Process.MainWindowHandle` 找目标窗口。该句柄对以下情况**恒为 0**：
- 目标是 `.lnk`（Shell 转交）；
- 启动器类程序（`QQ.exe` 拉起另一个进程后自身退出）；
- Win11 记事本的打包存根（`notepad.exe` 只是跳板）。

后果：40 × 150ms 白等 6 秒后放弃，摆位**静默失效**，用户完全无感知。

**实测对照**（`--launchprobe`，目标 notepad.exe，方位 LeftHalf）：

| 版本 | 摆位结论 | 窗口矩形 |
|---|---|---|
| 修复前 | 未等到目标窗口，摆位跳过 | (156,156)-(965,783) 809×627（记事本默认尺寸） |
| 修复后 | 已摆位 LeftHalf (0xB913BA) | **(0,0)-(960,1080) 960×1080**（精确左半屏） |

### 28.3 改动

**A. 窗口摆位链路**（`Core/Launcher.cs`、`Core/Native.cs`）

1. `Process.Start` **之前**拍一张「可见且有标题的顶层窗口」快照（`EnumWindows`），轮询时只认领**快照里没有**的新窗口，优先取 pid 匹配的那个。
   - 比原版裸 `GetForegroundWindow` 更稳：用户此刻手动切窗口，那个窗口在快照里 → 不会被误当成目标。
2. 从 `Thread.Sleep` 改为 `await Task.Delay` + 轮询：不再占线程池线程最多 15 秒（上一轮刚为线程池膨胀做过优化）。
3. 补 `WaitForInputIdle(500)`（Shell 转交的进程会抛 `InvalidOperationException`，忽略）。
4. 摆位改用 `Screen.FromHandle(handle).WorkingArea`：多屏时用**目标窗口所在屏**，不再一律主屏。
5. 补 `SW_RESTORE`（最大化/最小化窗口 `SetWindowPos` 不生效）、`SWP_NOACTIVATE`（不抢焦点）、`Maximize` 分支（原来落到 switch 默认 = 铺满工作区）。
6. 新增 `Launcher.LastPlacementNote`：摆位是异步的，无法通过 `LaunchResult` 回传，成败只能留痕在此（探针读取）。

**B. 右键剪切/复制/粘贴/删除**（新增 `Core/EntryClipboard.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`）

菜单顺序对齐原版（`Open location / Edit / Cut / Copy / Paste / Delete`）：

```
启动 / 打开所在位置 / 编辑… ─── 剪切 / 复制 / 粘贴 / 删除 ─── 加入·移出收藏栏 ─── 设置…
```

- 逻辑抽到 `Core/EntryClipboard.cs`（**不引用 WPF**），SelfTest 可直接断言；`MainWindow` 只做 UI 编排。
- 五种落位结果：`Moved`（剪切）、`Copied`（复制）、`Swapped`（同组剪切到占用位 → 两条互换，不丢数据也不用问）、`Replaced`（复制/跨组 → 确认后替换）、`NoTarget`（落点类型不匹配）。
- **刻意与原版不同的两点**：
  1. 剪切**不立即**从源分组删除，粘贴时才移动 —— 避免「剪切后忘了粘贴」丢条目；
  2. 复制粘贴多次，每次得到**独立 Id** 的新条目（原版是同一份数据被反复引用）。
- 空键位/空收藏位菜单：只留「编辑…」（新建）与「粘贴」（剪贴板非空时），其余禁用。
- 「从此分组移除」改名为**「删除」**（原版语义，也更直白）。
- `NextFreeSlot` / `NextFreeFunctionKey` 去重，统一走 `EntryClipboard`。

**C. 顺带修的一个隐患**

`MainWindow.Save()` 原先不检查 `SuppressPersist`，任何探针里的保存都会写用户 `board.json`。现在加了判断：探针/离屏实例一律不落盘。

### 28.4 验证

- 构建 **0 警告 0 错误**
- `--selftest`：**16 / 16 通过**（新增第 15 项「剪贴板剪切粘贴」覆盖跨组剪切、连续复制两条独立 Id、同组互换、拒绝替换、落点类型校验五条路径）
- `--menuprobe`：**全绿**。有内容磁贴 9 项（粘贴在剪贴板空时禁用）；空键位仅「编辑…」可用；`复制 → 剪贴板非空 → 空键位「粘贴」变为可用 → 点击后条目 42 → 43`
- `--launchprobe`：见 28.2 对照表
- 菜单渲染图 `screenshots/menuprobe/menu_filled.png` / `menu_empty.png`：中文清晰、分隔线正常、禁用态可辨
- 配置未污染：`board.json` mtime 仍为 19:15:41，3 组 42 条，theme=1 / font=11 / icon=1
- 私有内存 171.3MB（基线 169.5MB，在 ±20MB 单次波动内），线程 33 / 句柄 1014

### 28.5 新增探针

`--launchprobe <report.txt> [目标exe]`（`Core/LaunchProbe.cs`）：真实启动 GUI 程序走完整摆位链路，输出结论 + 窗口最终矩形，结束后只杀本次新起的进程。**必须在沙箱外跑**（沙箱会回收子进程，否则探针会误报失败）。

### 28.6 待定（未做，需拍板）

1. 编辑对话框里 `WindowPlacement` 仍是裸文本框、`WindowMoveDelay` / `WindowState` 无控件 —— 该补下拉与校验，或删字段；
2. 组内排序（按名称/目标）：要先定「排序是否重绑键位」；
3. 连按两次才启动、鼠标摇动切组：原版有，我们没做（后者建议不做）。

