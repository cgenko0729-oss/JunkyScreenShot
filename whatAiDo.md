# JunkyScreenShot 开发记录（whatAiDo.md）

本文件记录 AI 在开发 JunkyScreenShot 过程中做了什么、为什么这样做、影响了哪些文件，以及当前已知问题和 TODO。

---

## 项目目标

用 C# + WPF (.NET 8) 做一个轻量级的 Windows 截图工具，核心流程类似 Snipaste：

1. 程序在后台运行（托盘图标）。
2. 按 F1 进入截图模式，屏幕"冻结"并变暗。
3. 鼠标移动时自动检测光标下的窗口并用蓝色边框高亮。
4. 也可以按住左键拖拽手动框选区域。
5. 选定区域后在附近弹出小工具条：Pen / Copy / Save / QuickSave / Pin / Cancel。
6. Pin 会创建一个置顶的贴图窗口，可拖动、滚轮缩放、双击关闭，也可右键选择 Delete Pin 删除。
7. Esc 随时取消截图模式。

设计原则：KISS，不过度设计，不拆太多类，第一版不做标注功能。

---

## 开发计划

1. ✅ 创建 whatAiDo.md 并写下计划（本文件）
2. ✅ 创建 WPF 项目结构（csproj / app.manifest / App.xaml）
3. ✅ 实现后台运行 + 托盘图标（用 WinForms NotifyIcon，无需额外 NuGet 包）
4. ✅ 实现 F1 全局热键（RegisterHotKey + 消息窗口）
5. ✅ 实现截图遮罩窗口（先截全屏 → 作为背景 → 盖半透明黑层）
6. ✅ 实现手动拖拽框选（蓝框 + 尺寸提示）
7. ✅ 实现窗口自动检测（EnumWindows + DWM 窗口边界）
8. ✅ 实现选区确认后的工具条（Copy / Save / Pin / Cancel）
9. ✅ 实现 Copy（剪贴板）、Save（PNG）、Pin（置顶贴图窗口）
10. ✅ 编译通过（0 警告 0 错误）+ 启动冒烟测试通过；完整交互流程待人工验证
11. ✅ 更新本文件，记录完成情况和遗留 TODO

---

## 文件结构与职责

| 文件 | 职责 |
|---|---|
| `JunkyScreenShot.csproj` | 项目文件，net8.0-windows，启用 WPF + WinForms，并把 `app.ico` 嵌入 EXE |
| `app.manifest` | 声明 PerMonitorV2 DPI 感知，保证截图坐标与物理像素对应 |
| `App.xaml` / `App.xaml.cs` | 程序入口。无主窗口（ShutdownMode=OnExplicitShutdown），负责：托盘图标、F1 全局热键注册、启动截图模式 |
| `NativeMethods.cs` | 所有 Win32 API 声明（RegisterHotKey、EnumWindows、DwmGetWindowAttribute 等），集中放一处 |
| `CaptureOverlay.xaml` / `.cs` | 截图核心：全屏遮罩窗口。负责屏幕截取、变暗效果、窗口检测高亮、拖拽框选、画笔、工具条、复制、保存和置顶动作 |
| `PinWindow.xaml` / `.cs` | 贴图窗口：无边框、置顶、可拖动、双击关闭、滚轮缩放、右键菜单删除 |
| `whatAiDo.md` | 本开发记录文件 |

类的数量刻意控制在 4 个（App / NativeMethods / CaptureOverlay / PinWindow），不做 MVVM，逻辑写在 code-behind。

---

## 关键技术决策

- **不用隐藏主窗口**：App.xaml 去掉 StartupUri，热键用一个 message-only 窗口（HwndSource）接收 WM_HOTKEY，比隐藏 MainWindow 更干净。
- **托盘图标**：启用 UseWindowsForms 用 NotifyIcon 实现，这是 .NET 自带能力，不算第三方依赖。图标暂用系统默认应用图标（SystemIcons.Application），省去做 .ico 资源。
- **"冻结屏幕"效果**：进入截图模式先用 Graphics.CopyFromScreen 截下整个主屏，把截图铺满遮罩窗口做背景，再叠一层半透明黑色 Path；选区部分用 CombinedGeometry(Exclude) 在黑层上"挖洞"，露出明亮的原图。
- **窗口检测**：不用 WindowFromPoint（它会命中我们自己的全屏遮罩），改用 EnumWindows 按 Z 序从上往下找第一个包含鼠标点的可见窗口，跳过自己的遮罩窗口、最小化窗口和 UWP 隐身（cloaked）窗口。窗口矩形优先取 DWMWA_EXTENDED_FRAME_BOUNDS（不含阴影），失败再退回 GetWindowRect。
- **裁剪来源**：从进入截图模式时冻结的那张截图上裁剪（CroppedBitmap），而不是确认时再截一次，保证"所见即所得"。
- **DPI 处理**：manifest 声明 PerMonitorV2；遮罩窗口尺寸用 DIP，截图位图是物理像素，用 `scale = 位图像素宽 / 窗口DIP宽` 做坐标换算。

---

## 开发日志

### 2026-07-09 第 1 步：初始计划
- **做了什么**：创建本文件，写下完整开发计划、文件结构和技术决策。
- **为什么**：按需求先规划再动手，方便追踪。
- **影响文件**：`whatAiDo.md`（新建）

### 2026-07-09 第 2 步：项目骨架 + 后台运行 + 全局热键
- **做了什么**：
  - 创建 net8.0-windows WPF 项目文件，同时开启 UseWindowsForms（只为 NotifyIcon 和 Screen/Graphics）。
  - app.manifest 声明 PerMonitorV2 DPI 感知。
  - App.xaml 去掉 StartupUri，ShutdownMode 设为 OnExplicitShutdown → 程序无主窗口、纯后台运行。
  - App.xaml.cs 实现托盘图标（右键菜单：Capture / Exit，双击也可截图）和 F2 全局热键（message-only 窗口接收 WM_HOTKEY）。
  - 热键注册失败时弹警告但不退出（托盘菜单仍可用）。
- **为什么**：托盘 + 热键是"后台常驻截图工具"的基础；message-only 窗口比隐藏 MainWindow 更简单干净。
- **影响文件**：`JunkyScreenShot.csproj`、`app.manifest`、`App.xaml`、`App.xaml.cs`、`NativeMethods.cs`（均新建）

### 2026-07-09 第 3 步：截图遮罩（核心）
- **做了什么**：实现 CaptureOverlay 全屏遮罩窗口，一个类完成全部截图交互：
  - 打开时先 CopyFromScreen 截取主屏 → 铺满窗口背景 → 叠半透明黑层（选区处用 Exclude 几何挖洞提亮）。
  - 三个状态：Hover（鼠标移动时 EnumWindows 检测光标下窗口并蓝框高亮）、Dragging（拖拽画蓝色选框 + 实时显示 "宽 x 高 px"）、Selected（弹出工具条）。
  - 单击 = 确认高亮窗口区域；拖拽超过 5x5 像素 = 确认自定义区域；更小的拖拽忽略。
  - 工具条 4 个按钮：Copy（Clipboard.SetImage）、Save（SaveFileDialog 存 PNG，默认文件名 screenshot_yyyyMMdd_HHmmss.png，取消对话框则留在截图模式）、Pin（创建贴图窗口）、Cancel。
  - Esc / 右键 随时取消。工具条弹出后再按下鼠标 = 重新开始选择。
  - 裁剪一律从冻结的截图位图上做（CroppedBitmap），保证所见即所得。
- **为什么**：把截图交互全部收在一个类里，符合 KISS，避免 manager 类满天飞。
- **影响文件**：`CaptureOverlay.xaml`、`CaptureOverlay.xaml.cs`（新建）

### 2026-07-09 第 4 步：贴图窗口
- **做了什么**：PinWindow —— 无边框、置顶、按原选区大小显示图片，左键拖动（DragMove），双击关闭（ClickCount==2），滚轮缩放 0.2x~5x（可选功能，实现成本极低所以顺手做了）。
- **为什么**：满足需求 I / 12 / 13 及可选的滚轮缩放。
- **影响文件**：`PinWindow.xaml`、`PinWindow.xaml.cs`（新建）

### 2026-07-09 第 5 步：编译与冒烟测试
- **做了什么**：
  - `dotnet build` 编译通过。修掉唯一警告 WFAC010（WinForms 分析器建议用 API 设 DPI，但 WPF 只认 manifest，故在 csproj 中 NoWarn 并注释原因）。最终 0 警告 0 错误。
  - 启动冒烟测试：运行 exe 4 秒确认进程存活、无启动崩溃，然后结束进程。
- **为什么**：保证项目在 Visual Studio / dotnet CLI 下都能直接构建运行。
- **影响文件**：`JunkyScreenShot.csproj`（加 NoWarn）
- **说明**：F2 → 高亮/拖拽 → Copy/Save/Pin 的完整交互流程需要人工在真机上操作验证，AI 无法模拟按键与鼠标，请按下方"如何测试"手动过一遍。

### 2026-07-09 第 6 步：选区确认后支持 Ctrl+C 复制
- **做了什么**：
  - 在 CaptureOverlay 的 OnPreviewKeyDown 中加入 Ctrl+C 处理：当状态为 Selected（工具条已弹出）时，Ctrl+C 等同于点击 Copy 按钮。
  - 把复制逻辑抽成共用方法 `CopySelectionAndClose()`，Copy 按钮和 Ctrl+C 快捷键都调用它，避免重复代码。
- **为什么**：用户要求截图确认后除了点 Copy 按钮，也能直接按 Ctrl+C 复制，更顺手。
- **影响文件**：`CaptureOverlay.xaml.cs`
- **说明**：重新编译时旧实例还在运行导致 exe 被锁（MSB3027），已结束旧进程后编译通过（0 警告 0 错误）并重新启动了新版本。

### 2026-07-09 第 7 步：画笔标注功能（参考 Snipaste）
- **做了什么**：
  - 工具条新增 **Pen**（画笔开关）和 **Undo**（撤销）按钮，顺序：Pen / Undo / Copy / Save / Pin / Cancel。
  - 开启画笔后，工具条下方弹出**调色板**：3 档粗细（2/4/8）+ 15 种颜色，选中项用蓝色边框标识，当前颜色同时显示在 Pen 按钮文字上；调色板按钮在代码里循环生成（BuildPenPalette），不写一大坨 XAML。
  - 画笔模式下在选区内按住左键即可手写（Polyline 逐点添加），笔迹用 StrokeLayer.Clip 裁剪在选区内；鼠标指针变为笔形。
  - **Ctrl+Z** 或 Undo 按钮撤销上一笔；右键在画笔模式下先退出画笔（再按右键才是取消截图）。
  - 画笔模式下点击不会重新框选；关闭画笔后点选区外重新框选时会清空旧笔迹。
  - 导出合成：GetSelectedImage 在有笔迹时用 DrawingVisual + RenderTargetBitmap 把"裁剪的截图 + 所有笔迹"按物理像素合成，所以 **Copy / Ctrl+C / Save / Pin 出来的图都带笔迹**。
- **为什么**：用户要求像 Snipaste 一样能用画笔在截图上写字做标记；第一版只做画笔（最常用），其余标注工具见 TODO。
- **影响文件**：`CaptureOverlay.xaml`（新增 StrokeLayer 图层、Pen/Undo 按钮、PenPalette 面板）、`CaptureOverlay.xaml.cs`（画笔状态机、调色板、撤销、导出合成）
- **说明**：编译通过（0 警告 0 错误），已重启新版本，交互效果需人工验证。

### 2026-07-09 第 8 步：接入 Git 版本控制并推送 GitHub
- **做了什么**：
  - 新建 `.gitignore`（排除 bin/、obj/、.vs/ 等构建产物）。
  - `git init`（默认分支 main）→ 初始提交（12 个文件）→ 推送到 https://github.com/cgenko0729-oss/JunkyScreenShot.git。
- **为什么**：用户要求上传到 GitHub 公开仓库，开始使用版本控制。
- **影响文件**：`.gitignore`（新建）、`.git/`（仓库初始化）
- **约定**：**从现在起，每个新功能都在 feature 分支上开发**（`feature/<功能名>`），完成后合并回 main 再推送。

### 2026-07-09 第 9 步：QuickSave 快速保存 + 托盘设置默认文件夹（分支 feature/quicksave）
- **做了什么**：
  - 工具条在 Save 旁新增 **QuickSave** 按钮：不弹对话框，直接把 PNG 存到默认文件夹（文件名 screenshot_yyyyMMdd_HHmmss.png，同秒重名时自动加 _1、_2 后缀），保存后关闭截图模式。
  - 托盘菜单在 Capture 和 Exit 之间新增 **Set QuickSave Folder...**：用 FolderBrowserDialog 选择并保存默认文件夹。
  - 设置持久化走最简单方案：把路径写进 `%AppData%\JunkyScreenShot\quicksave_folder.txt`（一行文本），读写逻辑作为两个静态方法放在 App 里，不新建 Settings 类。
  - 未设置过时默认用 `图片\JunkyScreenShot`，首次 QuickSave 自动建目录。
  - 顺便重构：PNG 编码写盘抽成 `SavePng(path)`，Save 和 QuickSave 共用。
- **为什么**：用户要求常用保存不必每次选文件夹；默认文件夹要能在托盘里设置和修改。
- **影响文件**：`App.xaml.cs`（设置读写 + 托盘菜单项）、`CaptureOverlay.xaml`（QuickSave 按钮）、`CaptureOverlay.xaml.cs`（QuickSave 逻辑 + SavePng 重构）
- **说明**：本功能按新约定在 `feature/quicksave` 分支开发后合并回 main。编译 0 警告 0 错误，已重启新版本；QuickSave 落盘和托盘设置需人工验证。

### 2026-07-10 第 10 步：发布为独立单文件 exe（分支 feature/publish-exe）
- **做了什么**：
  - 用 `dotnet publish` 发布自包含单文件版：`-c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none`。
  - 产物是**一个 68 MB 的 JunkyScreenShot.exe**（`bin\Release\net8.0-windows\win-x64\publish\`），内含 .NET 8 运行时，拷到任何 64 位 Windows 上双击即可用，对方无需装 .NET、无需终端。
  - 新增 `publish.cmd` 脚本：以后双击它就能重新打包。
- **踩坑记录（重要）**：
  - 第一次用常规单文件参数 `IncludeNativeLibrariesForSelfExtraction=true` 打包，程序**启动即崩溃**（事件日志：`DllNotFoundException` 于 WindowsBase）。这是 WPF 的已知限制——WPF 的原生 DLL（wpfgfx_cor3.dll、PresentationNative_cor3.dll 等）不支持这种打包方式。
  - 解决办法：改用 `IncludeAllContentForSelfExtract=true`（.NET Core 3.1 式整体自解压，首次运行解压到 %TEMP%，WPF 兼容）。验证通过：启动后 12 秒存活、事件日志无新崩溃。
- **为什么**：用户想把程序分发给不会用终端的人，双击 exe 就能用。
- **影响文件**：`publish.cmd`（新建）、`whatAiDo.md`
- **说明**：程序没有单实例保护，双开会导致 F1 热键冲突（第二个实例会弹警告）和双托盘图标，已记入 TODO。

### 2026-07-18 第 11 步：简化画笔撤销、贴图右键删除、F1 快捷键、QuickSave 路径复制
- **做了什么**：
  - 移除工具条上的 Undo 按钮，画笔撤销只保留 **Ctrl+Z**。
  - 置顶贴图增加右键菜单 **Delete Pin**，点击后关闭并删除该贴图窗口。
  - 全局截图快捷键由 **F2** 改为 **F1**，并同步修改托盘菜单、托盘提示和注册失败提示。
  - QuickSave 保存到自定义文件夹后，把生成的 PNG 完整路径复制到剪贴板；如果仅路径复制失败，会明确提示图片已经保存。
- **影响文件**：`CaptureOverlay.xaml`、`CaptureOverlay.xaml.cs`、`PinWindow.xaml`、`PinWindow.xaml.cs`、`NativeMethods.cs`、`App.xaml.cs`、`whatAiDo.md`
- **说明**：`dotnet build` 已通过，0 警告、0 错误；快捷键和鼠标交互仍需在 Windows 桌面上人工确认。

### 2026-07-18 第 12 步：QuickSave / Save As 快捷键和工具栏悬停提示
- **做了什么**：
  - 选定截图区域后，按 **Ctrl+S** 直接 QuickSave 到自定义文件夹，并复制完整图片路径。
  - 选定截图区域后，按 **Ctrl+Shift+S** 打开普通 Save As 对话框。
  - 工具栏按钮增加悬停提示：Copy 显示 Ctrl+C、Save 显示 Ctrl+Shift+S、QuickSave 显示 Ctrl+S、Cancel 显示 Esc；没有快捷键的 Pen 和 Pin 也显示功能说明。
  - 保存按钮和快捷键复用相同方法，确保鼠标与键盘操作行为一致。
- **影响文件**：`CaptureOverlay.xaml`、`CaptureOverlay.xaml.cs`、`whatAiDo.md`
- **说明**：`dotnet build` 已通过，0 警告、0 错误。

### 2026-07-18 第 13 步：自定义 EXE 和系统托盘图标
- **做了什么**：
  - 把用户提供的 `screenshotappicon.png` 转换为多尺寸 `app.ico`，内含 16/20/24/32/40/48/64/128/256 像素版本。
  - 在项目文件中设置 `ApplicationIcon`，让发布后的 EXE 和文件资源管理器显示自定义图标。
  - 托盘图标不再使用 Windows 默认应用图标，改为从当前 EXE 提取同一个自定义图标。
  - 应用退出时释放图标资源，避免原生图标句柄泄漏。
- **影响文件**：`screenshotappicon.png`、`app.ico`、`JunkyScreenShot.csproj`、`App.xaml.cs`、`whatAiDo.md`
- **说明**：`dotnet build` 已通过，0 警告、0 错误，并确认构建出的 EXE 内含可提取的 32×32 图标。

---

## 如何测试

1. `dotnet run` 或在 Visual Studio 打开 `JunkyScreenShot.csproj` 按 F5。
2. 程序无窗口，看系统托盘出现图标即为启动成功。
3. 按 **F1**（或托盘菜单 Capture）：屏幕冻结变暗。
4. 移动鼠标：光标下的窗口出现蓝框；**单击**即选中该窗口区域。
5. 或按住左键**拖拽**：画出蓝色选框，旁边显示 "宽 x 高 px"。
6. 松开后出现工具条：Pen / Copy / Save / **QuickSave** / Pin / Cancel。悬停按钮会显示功能及其快捷键；可按 **Ctrl+C** 复制、**Ctrl+S** QuickSave、**Ctrl+Shift+S** 另存为。QuickSave 不弹窗，直接存到默认文件夹（托盘菜单 **Set QuickSave Folder...** 可修改，未设置时默认为 图片\JunkyScreenShot），并把保存后的完整路径复制到剪贴板。
7. 点 **Pen** 开启画笔：下方出现调色板（3 档粗细 + 15 色），在选区内按住左键手写；按 **Ctrl+Z** 撤销上一笔；右键退出画笔模式。笔迹会包含在 Copy / Save / Pin 的结果里。
8. Pin 出来的贴图窗口：左键拖动，滚轮缩放，双击关闭，或右键选择 **Delete Pin** 删除。
9. **Esc** 或右键（非画笔模式下）随时取消截图模式。托盘菜单 Exit 退出程序。

---

## 已知问题 / TODO

- [ ] **多显示器**：第一版只支持主显示器截图（遮罩只盖主屏）。多屏 + 混合 DPI 的坐标换算复杂，留到后续版本。
- [ ] **完整交互流程未经人工验证**：编译和启动测试已通过，但 F1 → 选区 → Copy/Save/Pin 的真机操作需要用户手动过一遍（见上方"如何测试"）。
- [x] ~~托盘图标使用系统默认应用图标~~ 已改为自定义 `app.ico`，EXE 与托盘共用同一图标（第 13 步）。
- [ ] Pin 之后再截图时，贴图窗口本身会被当作普通窗口检测到/截进去 —— 这与 Snipaste 行为一致，暂视为特性而非 bug。
- [ ] 窗口检测只识别顶层窗口，不识别子控件/面板区域（第一版按需求刻意从简）。
- [ ] 剪贴板偶发被其他程序占用会导致 Copy 失败（已 try-catch 弹提示，未做自动重试）。
- [x] ~~第一版没有标注功能~~ 已加入**画笔**标注（第 7 步）。
- [ ] 其他标注工具未做：箭头、矩形/椭圆框、文字、马赛克、橡皮擦、Redo（重做）。参考图里的完整工具条留待后续版本。
- [ ] 画笔粗细只有 3 档固定值（2/4/8），没有自定义取色器。
- [ ] **没有单实例保护**：双开程序会导致 F1 热键冲突和双托盘图标，后续可用 Mutex 实现"已运行则不再启动"。

---

### 2026-07-21 第 14 步：把构建好的 exe 直接提交到仓库（分支 feature/dist-exe）

- **做了什么**：用 `publish.cmd` 同样的参数跑 `dotnet publish` 生成自包含单文件 exe，拷贝到新建的 `dist/JunkyScreenShot.exe`（不在 `.gitignore` 排除的 `bin/` 里），直接提交进仓库并推送到 GitHub。
- **为什么**：用户想要任何人打开仓库就能直接下载到 exe 双击运行，不想走 GitHub Release 手动上传流程。
- **影响文件**：`dist/JunkyScreenShot.exe`（新建，~68 MB）
- **注意**：这个二进制文件不会随代码改动自动更新，以后每次发布新版本都要重新 `dotnet publish` 并覆盖 `dist/JunkyScreenShot.exe` 再提交。
