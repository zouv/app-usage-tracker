# 代码链路图谱 · app-usage-tracker

> 面向 AI 的代码导航：先按 §0.5 定作用域，再按 §1 文件职责与 §2 函数入口定位，避免全量扫描源码。
> 代码结构、接口或关键链路变化后同步本文（见 AGENTS §1）；长期坑与设计理由归 §7，日期、版本和改动经过归 CHANGELOG/git log。
> 全文不超过 25,000 字；函数索引使用名称而非行号。图谱不是权威真相，与代码不符时以代码为准并顺手订正。

## 0. 一句话架构

.NET 8 WPF 单主窗口应用，通过 MVVM 组织界面，以 Win32 前台窗口事件驱动活动会话，使用 JSON 本地持久化原始会话和统计数据。

```text
App
 ├─ LocalizationService ── Strings/（嵌入式中英文文案）
 └─ MainWindow
     └─ MainViewModel
         ├─ OverviewViewModel
         ├─ AppsViewModel
         ├─ StatisticsViewModel
         ├─ TimelineViewModel
         ├─ TimerViewModel
         └─ SettingsViewModel

AppRuntime（共享单例）
 ├─ ForegroundWindowMonitor ── WinEventHook
 ├─ IdleStateMonitor ── GetLastInputInfo
 ├─ SystemSessionMonitor ── SessionSwitch / PowerModeChanged
 ├─ ApplicationMatcher ── 路径 / 进程名 / 窗口标题
 ├─ ActivitySessionService ── 状态机 / 心跳 / 跨天 / 恢复
 ├─ StatisticsService ── 日周月年与累计聚合
 ├─ JsonAppDataStore ── 原子写入 / 备份 / 恢复
 ├─ SessionEditor / CsvExportService
 └─ TrayIconService / UsageNotificationService
```

## 0.5 作用域路由

| 任务类型 | 优先读取 | 通常忽略 |
| --- | --- | --- |
| 主窗口和导航 | `src/AppUsageTracker/MainWindow.*`、`Themes/`、相关 ViewModel | 数据存储实现 |
| 主题与配色 | `Services/ThemeService.cs`、`Themes/Palette.*.xaml`、`Themes/Theme.xaml` | 统计与会话逻辑 |
| 界面语言与文案 | `Services/LocalizationService.cs`、`Strings/Strings.*.xml`、`Converters/LocalizeConverter.cs` | 统计与会话逻辑 |
| 软件配色与图标 | `Services/AppColorPalette.cs`、`Services/AppIconProvider.cs` | 统计聚合 |
| 柱状图与缩放 | `Controls/ZoomableBarChart.cs`、`Controls/ChartLegend.cs`、`ViewModels/Chart*.cs` | 持久化实现 |
| 时间线排版 | `ViewModels/ChartBuilder.cs`、`ViewModels/TimelineLayout.cs`、`Views/OverviewView.xaml` | 持久化实现 |
| 图标与资源 | `tools/generate-icon.ps1`、`Assets/`、`*.csproj` | 业务服务 |
| 前台监听 | `Services/ForegroundWindowMonitor*`、`Services/ApplicationMatcher*` | 统计页视觉 |
| 运行模式进程扫描 | `Services/ProcessScanner*`（`EnumerateRunningProcesses`）、`TrackingCoordinator` | 统计页视觉 |
| 空闲、锁屏、休眠 | `Services/IdleStateMonitor*`、`Services/SystemSessionMonitor*` | 软件管理界面 |
| 会话与心跳 | `Services/ActivitySessionService*`、`Models/ActivitySession*` | 托盘视觉 |
| 数据持久化 | `Services/JsonAppDataStore*`、`Models/` | XAML 页面 |
| 统计查询 | `Services/StatisticsService*`、统计模型和测试 | Win32 封装 |
| 软件管理 | `Views/AppsView*`、`ViewModels/AppsViewModel*`、`TrackedApp*` | 时间线编辑 |
| 桌面倒计时定时器 | `Services/CountdownTimerService.cs`、`ViewModels/TimerViewModel.cs`、`Views/TimerView.*`、`Views/CountdownPanelWindow.*`、`Views/TimerOverlayWindow.*` | 统计聚合 |
| 构建发布 | `manager.sh`、根目录 `*.bat`、`scripts/*.ps1`、`*.csproj` | 业务服务 |
| 产品范围 | `docs/design/software-usage-duration-design.md` | 生成目录 |

## 1. 当前文件职责

| 路径 | 状态 | 职责 |
| --- | --- | --- |
| `AppUsageTracker.sln` | 已有 | 主项目和测试项目解决方案 |
| `src/AppUsageTracker/App.xaml` | 已有 | WPF 应用入口和全局资源 |
| `src/AppUsageTracker/App.xaml.cs` | 已有 | 应用启动/退出、单实例守卫和主窗口呼出 |
| `src/AppUsageTracker/MainWindow.xaml` | 已有 | 单主窗口、左侧导航和页面承载 |
| `src/AppUsageTracker/AppUsageTracker.csproj` | 已有 | WPF、WinForms 托盘和 MVVM 依赖配置 |
| `tests/AppUsageTracker.Tests/` | 已有 | xUnit 测试项目 |
| `src/AppUsageTracker/Models/` | 已有 | 软件、规则、会话、设置和统计模型 |
| `src/AppUsageTracker/Services/` | 已有 | 监听、状态机、持久化、统计、托盘和导出 |
| `src/AppUsageTracker/ViewModels/` | 已有 | 六个页面和应用壳的状态与命令 |
| `src/AppUsageTracker/Views/` | 已有 | 概览、统计、软件管理、时间记录、定时器和设置 |
| `src/AppUsageTracker/Controls/ZoomableBarChart.cs` | 已有 | 自绘可缩放堆叠柱状图，滚轮缩放、拖拽平移、悬停高亮 |
| `src/AppUsageTracker/Controls/ChartLegend.cs` | 已有 | 图表图例，与柱状图双向联动高亮 |
| `src/AppUsageTracker/ViewModels/ChartLayout.cs` | 已有 | 缩放、平移、槽位合并的纯换算逻辑 |
| `src/AppUsageTracker/ViewModels/ChartBuilder.cs` | 已有 | 把会话与统计结果组装成图表数据 |
| `src/AppUsageTracker/ViewModels/ChartData.cs` | 已有 | 图表系列、槽位和数据集契约 |
| `src/AppUsageTracker/Services/AppColorPalette.cs` | 已有 | 8 槽位配色，按软件自动分配并随主题解析 |
| `src/AppUsageTracker/Services/AppIconProvider.cs` | 已有 | 软件图标解析与缓存 |
| `src/AppUsageTracker/Services/GlobalHotkeyService.cs` | 已有 | 全局快捷键注册到主窗口句柄并分发 WM_HOTKEY，托盘隐藏后仍可响应 |
| `src/AppUsageTracker/Services/CountdownTimerService.cs` | 已有 | 倒计时定时器：单调时间源算剩余、System.Timers.Timer 刷新，开始/暂停/继续/停止/结束事件 |
| `src/AppUsageTracker/ViewModels/TimerViewModel.cs` | 已有 | 定时器独立页状态与命令；桌面面板显隐、拖拽位置持久化 |
| `src/AppUsageTracker/Views/TimerView.xaml` | 已有 | 定时器独立导航页：剩余时间/状态、时长、开始/暂停/停止、面板与结束动作设置 |
| `src/AppUsageTracker/Views/CountdownPanelWindow.xaml` | 已有 | 桌面置顶可拖拽倒计时面板，宽度/文字与底板分层不透明度/自适应字号绑定设置 |
| `src/AppUsageTracker/Views/TimerOverlayWindow.xaml` | 已有 | 倒计时结束的全屏置顶桌布，底板不透明度可配、文字不透明，Esc 关闭 |
| `src/AppUsageTracker/Models/HotkeyDefinition.cs` | 已有 | 快捷键字符串与「修饰符 + 虚拟键码」的双向转换 |
| `src/AppUsageTracker/Themes/Theme.xaml` | 已有 | 公共控件样式，颜色一律 DynamicResource |
| `src/AppUsageTracker/Services/LocalizationService.cs` | 已有 | 界面语言服务，中英文切换、代码取文案 T()、日期格式与枚举显示名 |
| `src/AppUsageTracker/Strings/Strings.Chinese.xml` | 已有 | 中文界面字符串（唯一来源，嵌入资源） |
| `src/AppUsageTracker/Strings/Strings.English.xml` | 已有 | 英文界面字符串，键与中文一一对应 |
| `src/AppUsageTracker/Converters/LocalizeConverter.cs` | 已有 | 分类/统计模式等模型字段到当前语言显示名的转换 |
| `src/AppUsageTracker/Themes/Palette.Light.xaml` | 已有 | 浅色调色板（6 档灰阶 + 现代品牌紫），可被整体替换 |
| `src/AppUsageTracker/Themes/Palette.Dark.xaml` | 已有 | 深色调色板，键与浅色一一对应 |
| `src/AppUsageTracker/Themes/Icons.xaml` | 已有 | 集中式 StreamGeometry 图标资源字典（替代 Segoe MDL2 字符图标） |
| `src/AppUsageTracker/Assets/app.ico` | 已有 | 应用图标，用于 exe、窗口、侧边栏和托盘 |
| `tools/generate-icon.ps1` | 已有 | 可重现地生成多尺寸图标 |
| `tools/capture-ui.ps1` | 已有 | 启动应用、切页并截图，供 UI 实跑核对 |
| `docs/design/` | 已有 | 产品、技术和界面设计 |
| `manager.sh` | 已有 | build/start/test/icon/package/pack/clean/release 统一入口 |
| `scripts/` | 已有 | BAT 的 PowerShell 实现：构建、测试、运行状态、正常停止、安全清理与 dev/release 打包；打包事务与 zip 校验见 `PackageTransaction.ps1`，运行记录位于 `.runtime/desktop.json`，打包暂存位于 `.runtime/staging/` |
| 根目录 `*.bat` | 已有 | Windows 薄入口；多数经 `_ps.bat` 统一选择 PowerShell、转发参数与退出码，`package.bat` 直接调用 `scripts/package.ps1` 且不暂停，便于 CI/自动化 |

## 2. 按「我要做什么」反查代码位置（任务 → 入口）

> 只登记常见改动的入口函数或类型锚点，不是全量函数表。按名称搜索；搜不到时以代码为准，并在任务收尾时订正本表。

| 我要改的东西 | 位置 | 关键函数 / 锚点 |
| --- | --- | --- |
| 应用启动与初始化 | `App.xaml.cs`、`Services/AppRuntime.cs` | `App.OnStartup`、`AppRuntime.InitializeAsync` |
| 正常退出与会话收尾 | `App.xaml.cs`、`Services/ActivitySessionService.cs` | `App.ExitApplication`、`ActivitySessionService.StopAsync` |
| 主窗口导航与页面承载 | `MainWindow.xaml`、`ViewModels/MainViewModel.cs` | 页面导航属性与命令 |
| 前台窗口监听 | `Services/ForegroundWindowMonitor.cs`、`ApplicationMatcher.cs` | WinEventHook 封装、软件匹配入口 |
| Running 模式进程扫描 | `Services/ProcessScanner.cs`、`TrackingCoordinator.cs` | `ShouldScanRunningProcesses`、`EnumerateRunningProcesses`、`MatchRunningProcess`、`HandleRunningProcessesAsync` |
| 活动会话状态机与恢复 | `Services/ActivitySessionService.cs` | `HandleRunningProcessesAsync`、`RecoverOpenSessions`、`StopAsync` |
| 数据加载与原子保存 | `Services/JsonAppDataStore.cs` | `LoadAsync`、`WriteAtomicAsync` |
| 统计图表数据组装 | `ViewModels/ChartBuilder.cs`、`StatisticsViewModel.cs` | `BuildDaily`、`BuildWeekly`、`BuildByApp` |
| 柱状图缩放与绘制 | `Controls/ZoomableBarChart.cs`、`ViewModels/ChartLayout.cs` | `OnRender`、缩放/平移换算 |
| 界面语言与动态文案 | `Services/LocalizationService.cs`、`Strings/` | `Apply`、`T`、`LanguageChanged` |
| 明暗主题与资源字典 | `Services/ThemeService.cs`、`App.xaml`、`Themes/` | 调色板字典整体替换 |
| 全局快捷键呼出 / 隐藏 | `Services/GlobalHotkeyService.cs`、`App.xaml.cs` | WM_HOTKEY 分发、`App.ToggleMainWindow` |
| 托盘与通知 | `Services/TrayIconService.cs`、`UsageNotificationService.cs` | 托盘菜单、连续使用提醒、每日摘要 |
| 倒计时与结束动作 | `Services/CountdownTimerService.cs`、`ViewModels/TimerViewModel.cs`、`App.xaml.cs` | `Start/Pause/Resume/Stop/Tick`、`OnCountdownFinished`、`SystemSessionMonitor.TryLockWorkStation` |
| 会话编辑与导出 | `Services/SessionEditor.cs`、`CsvExportService.cs` | 补录、修改、合并、批量删除、CSV 导出；进行中会话会被拒绝 |
| 列表选择与危险确认 | `Views/AppsView*`、`TimelineViewModel.cs`、`StatisticsViewModel.cs`、`IConfirmationService.cs` | DataGrid 单选样式、删除/合并确认、排行榜 `SelectedAppRow` 与刷新后选择协调 |
| 构建、打包与清理 | `manager.sh`、根目录 `*.bat`、`scripts/*.ps1` | `cmd_build`、`cmd_test`、`cmd_package`、`cmd_release`、`cmd_clean`；`dev.ps1`、`desktop.ps1`、`package.ps1`、`PackageTransaction.ps1` |

## 3. 关键链路

### 3.1 前台活动会话

```text
WinEventHook
 -> ForegroundWindowMonitor
 -> ApplicationMatcher
 -> ActivitySessionService.EndCurrent()
 -> ActivitySessionService.StartMatched()
 -> JsonAppDataStore
```

### 3.1b 运行模式进程扫描（心跳驱动）

```text
TrackingCoordinator.HeartbeatTimer（重入保护）
 -> IActivitySessionService.ShouldScanRunningProcesses？无启用运行模式软件/暂停/锁屏则跳过
 -> IProcessScanner.EnumerateRunningProcesses(RunningProcessScanNames)（只查候选进程名，不读取窗口标题）
 -> ApplicationMatcher.MatchRunningProcess()（返回所有命中 TrackingMode.Running 的软件，忽略窗口标题规则）
 -> ActivitySessionService.HandleRunningProcessesAsync()（匹配集合未变化时直接返回）
 -> 前台命中与运行模式软件并行累计：同一软件只计一次，多软件各自独立会话
```

多软件并发累计：`ActivitySessionService` 按软件 Id 维护多条进行中的 Active 会话（`_appSessions` 字典），全局状态会话（空闲/锁屏/休眠/暂停）单独一条；隐私模式只作为阻断态，不写状态会话。前台命中优先作为快照主显示项，与后台运行软件同时各自累计；锁屏/休眠/暂停/隐私一次性结束全部软件会话。快照 `TrackingSnapshot.ActiveApps` 携带全部活跃软件（含来源模式），`CurrentApp/CurrentSession` 保留为主显示项（供托盘/通知/添加软件等单值消费）。

### 3.2 空闲和系统状态

```text
GetLastInputInfo / SessionSwitch / PowerModeChanged
 -> 状态变化
 -> ActivitySessionService
 -> 结束或恢复有效会话
```

### 3.3 统计查询

```text
ActivitySession[]
 -> StatisticsService
 -> 周期聚合模型
 -> StatisticsViewModel
 -> ChartBuilder
 -> ZoomableBarChart / ChartLegend
```

周期到图表的分派：`日`、`全部` 走 `BuildByApp`（按软件一根柱），
`年` 走 `BuildWeekly`（按周一根柱），其余走 `BuildDaily`（按天一根柱）。

### 3.4 全局快捷键

```text
RegisterHotKey
 -> GlobalHotkeyService（主窗口句柄 WM_HOTKEY）
 -> App.ToggleMainWindow（可见则隐藏、不可见则呼出）
```

### 3.5 界面语言切换

```text
设置页语言下拉（Language 即时生效）
 -> LocalizationService.Apply
 -> 整体替换应用资源合并字典第 1 项（Strings.Chinese / Strings.English）
 -> XAML DynamicResource 自动重绘；LanguageChanged 事件通知 ViewModel 与托盘重算动态文案
```

### 3.6 应用启动与退出

```text
App.OnStartup
 -> 单实例守卫（命名 Mutex；已运行则经命名 EventWaitHandle 唤醒首实例并退出）
 -> JsonAppDataStore.LoadAsync
 -> AppRuntime.InitializeAsync
 -> ActivitySessionService.RecoverOpenSessions
 -> TrackingCoordinator.Start
 -> MainWindow / TrayIconService / CountdownTimerService（TimerViewModel）

App.ExitApplication
 -> ActivitySessionService.StopAsync
 -> 保存设置 / 软件 / 会话 / 修正
 -> 释放托盘、系统监听、倒计时定时器与单实例互斥体

Windows 注销 / 关机（SessionEnding）
 -> 同步等待 AppRuntime.DisposeAsync（最多 5 秒），不依赖 async void 退出链

关闭窗口（MinimizeToTrayOnClose=false）→ MainWindow.OnClosing → App.ExitApplication
恢复备份 → AppRuntime.PrepareForRestoreAsync（停协调器 + StopWithoutSaving）
        → JsonAppDataStore.ImportBackupAsync → App.ExitWithoutSaving
        → 导入失败则 ActivitySessionService.ResumeAsync + TrackingCoordinator.Start 续听
```

退出归因埋点（INFO）：`ExitApplication(reason)` / `ExitWithoutSaving(reason)` 记录调用原因（托盘菜单、
关闭按钮且未启用托盘驻留、恢复备份成功），`MainWindow.OnClosing` 记录「隐藏到托盘」或「转完整退出」分支，
`OnSessionEnding` 记录 `ReasonSessionEnding`，第二实例启动、`Application.Exit`（含退出码）与
`AppDomain.ProcessExit` 各记一条；`SystemSessionMonitor.SetState` 记录来源（会话切换原因 / 电源模式）与状态迁移。
锁屏分支只改状态、不接退出链；被外部终止时不会出现 Exit/ProcessExit 日志，据此区分「自走退出链」与「被结束」。

### 3.7 桌面倒计时定时器

```text
定时器独立页（MainViewModel.Timer → TimerViewModel，App.xaml DataTemplate 承载 TimerView）
 -> CountdownTimerService.Start（单调时间源算剩余，System.Timers.Timer 500ms 刷新）
 -> Changed → TimerViewModel 回 Dispatcher 刷新剩余时间、运行/暂停状态与面板显隐
 -> Finished → App.OnCountdownFinished 回 UI 线程：
     LockScreen → SystemSessionMonitor.TryLockWorkStation（user32.LockWorkStation 封装）
     ScreenOverlay → TimerOverlayWindow（主屏全屏、底板不透明度绑定 `OverlayOpacityFraction`、文字保持不透明、Esc 关闭）
```

面板（CountdownPanelWindow）根 Border 绑 `PanelWidth`，内部底板 Border 与文字 StackPanel 分层绑
`PanelBaseOpacityFraction` / `PanelTextOpacityFraction`（根 Border 背景 Transparent 承载逐层 alpha）；
剩余时间/状态字号由 `UpdateFontSizes` 自适应：`MeasureRemainingEm` 用 `FormattedText` 按面板数字同款字体
（`SystemFonts.MessageFontFamily` + SemiBold）实测 100px 字宽换算 em，再按可用宽度反推 `PanelFontSize`（夹 10–32）；
宽度 <120 时左右内边距由 14 收至 8（`PanelPadding`）。数字 TextBlock 必须 `TextWrapping="NoWrap"`
（隐式 TextBlock 样式全局 Wrap），状态文字可换行。拖拽结束回写
`AppSettings.TimerPanelLeft/Top`（nullable double，null 为默认位置）持久化。

设置项修改即时生效并走 `SaveSettingsAsync`：时长为时/分/秒三输入合成 `TotalSeconds`→`TimerDurationSeconds`；
面板开关/宽度（60–480）/文字不透明度（20–100）/底板不透明度（0–100）/结束动作；
桌布底板不透明度（10–100）行仅 `IsOverlayAction`（结束动作=全屏桌布）时显示。
按钮显隐互斥：空闲只显「开始」，运行中显「暂停/继续」+「停止」（`IsTimerActive`/`IsTimerPaused` 驱动）。
旧配置一次性迁移见 §7。

## 4. 数据契约

### ActivitySession

| 字段 | 规则 |
| --- | --- |
| `ApplicationId` | 对应已配置软件 |
| `StartedAtUtc` | UTC 开始时间 |
| `EndedAtUtc` | UTC 结束时间，可在活动中为空 |
| `DurationSeconds` | 使用单调时间计算 |
| `State` | Active、Idle、Locked、Suspended、Paused 等；来源统计模式记录在 `ActiveAppInfo.SourceMode` |
| `EndReason` | WindowChanged、Idle、Locked、Paused、Midnight、Recovered 等 |
| `LastHeartbeatAtUtc` | 异常恢复上界，也是实时时长的增量基准 |
| `IsManual` | 是否由用户补录或修改 |

> 会话集合只能通过 `IActivitySessionService` 的快照与编辑入口访问：读取用 `Sessions`（深拷贝）或 `AppRuntime.GetLiveSessionsSnapshot()`，修改用 `EditSessionsAsync` / `ClearSessionsAsync`。进行中的会话不允许手工编辑或删除。

### TrackingSnapshot

| 字段 | 规则 |
| --- | --- |
| `State` | 全局聚合状态：任一软件活跃即 Active，否则按空闲/锁屏/暂停等 |
| `ActiveApps` | 全部正在累计的软件（`ActiveAppInfo`：App + Session + 来源 TrackingMode），支持多软件并行 |
| `CurrentApp` / `CurrentSession` | 主显示项（前台命中优先，否则第一个活跃软件；无软件活跃时 CurrentSession 回退到全局状态会话），供托盘/通知等单值消费 |

## 5. 样式 / 资源索引

| 区段 | 位置 / 搜索锚点 | 内容与约束 |
| --- | --- | --- |
| 资源字典顺序 | `App.xaml` 的 `MergedDictionaries` | 第 0 位调色板、第 1 位语言字典由服务按下标替换；`Icons.xaml` 必须先于 `Theme.xaml` |
| 公共控件样式 | `Themes/Theme.xaml` | 颜色使用 `DynamicResource`；模板、圆角和字号遵循 §7 |
| 细滚动条 | `Themes/Theme.xaml` 的 `ScrollBar.Thin` | 排行等窄面板经 `ListBox.Resources` 套用，约默认 1/3 宽；复用默认模板，不重写 Track；排行卡片右内边距 8（Border Padding 覆盖 + ListBox 无右 margin） |
| 滚动条翻页按钮模板 | `Themes/Theme.xaml` 的 `ScrollBar.PageButton` | 垂直/水平四个 RepeatButton 共用的透明无触发器模板，去掉轨道悬停高亮，见 §7 |
| 明暗调色板 | `Themes/Palette.Light.xaml`、`Palette.Dark.xaml` | 两文件资源键一一对应，由 `ThemeService` 整体替换 |
| 矢量图标 | `Themes/Icons.xaml` 的 `Icon.*` | 统一 StreamGeometry，通过 Path 引用，不新增 MDL2 字符图标 |
| 本地化文案 | `Strings/Strings.Chinese.xml`、`Strings.English.xml` | 键一一对应；XAML 用 `Loc.*`，代码用 `LocalizationService.T` |
| 图表与软件配色 | `Controls/`、`Services/AppColorPalette.cs` | 自绘几何与固定 8 槽位，不为大量柱段创建可视元素 |

## 6. 运维 / 脚本

> 运维任务只读本节、`manager.sh`、相关 BAT/PowerShell 与 `docs/02-使用说明.md`，无需进入业务代码。具体操作以使用说明为准。

| 入口 | 职责 |
| --- | --- |
| `manager.sh` | build/start/test/icon/package（`pack` 别名）/release/clean/help 统一入口；版本号写入主项目 csproj |
| `_ps.bat`、`scripts/_common.ps1` | BAT 公共入口与 PowerShell 公共路径、进程、运行记录和受管删除 helpers；自动化用 `APP_USAGE_TRACKER_NO_PAUSE=1` 跳过暂停 |
| `build.bat`、`test.bat` | 调用 `scripts/build.ps1`、`test.ps1`；支持配置与追加 dotnet 参数，测试遍历所有测试项目 |
| `run.bat`、`dev.bat`、`stop.bat`、`status.bat` | 调用 `scripts/desktop.ps1`、`dev.ps1`；开发入口先强制关闭仓库内运行实例再构建 Debug；启动时写 `.runtime/desktop.json`，停止只请求正常关闭并等待，绝不强杀 |
| `package.bat`、`publish.bat` | `package.bat` 直接调用 `scripts/package.ps1`；产出 `dist/dev` 调试 zip 与 `dist/release/unpack` + zip，先写唯一暂存目录并校验版本/单文件/zip，再事务替换与回滚。`publish.bat` 为兼容别名 |
| `clean.bat`、`ci.bat` | 调用 `scripts/clean.ps1`；清理拒绝运行实例、越界路径和 reparse point，CI 禁用常驻构建服务器并跳过子 BAT 暂停 |
| `tools/generate-icon.ps1` | 可重现地生成应用图标 |
| `tools/capture-ui.ps1` | 启动、切页与截图，辅助 UI 实跑核对 |
| `check-docs.sh` | 文档机械检查；`--hook` 供 Claude PostToolUse 调用 |

## 7. 易踩的坑 / 长期约束

- 不要把”进程存在”当作默认有效使用。
- 不要直接使用系统时钟差值计算持续时间。
- 不要在 WinEvent 回调中执行磁盘 IO 或复杂查询。
- 不要在 View Code-behind 中维护监听状态。
- 不要直接覆盖正式 JSON 文件。
- 不要让多个 ViewModel 分别创建监听服务实例。
- 不要在系统事件回调线程中直接更新 WPF 控件。
- 不要把窗口标题写入持久化会话；标题只用于内存匹配。
- 不要用 `StaticResource` 引用 `Brush.*`；主题切换依赖 `DynamicResource` 才能即时重绘。
- 不要改动调色板字典在 `App.xaml` 合并字典中的第 0 位；`ThemeService` 按下标整体替换。
- 不要改动字符串字典在合并字典中的第 1 位（由 `LocalizationService` 启动时插入并整体替换）。
- 界面文案只加到 `Strings/Strings.Chinese.xml` 与 `Strings/Strings.English.xml`，键一一对应；代码取文案用 `LocalizationService.T`，XAML 用 `{DynamicResource Loc.*}`。
- 不要在 `Strings/` 里使用带语言标签的文件名（如 `Strings.zh-CN.xml`）；MSBuild 会把它当作卫星资源而不嵌入主程序集。
- 不要给柱状图的柱子创建可视元素；成百上千个柱段必须走 `OnRender` 直接出几何。
- 不要把概览页和统计页整体套进 `ScrollViewer`；图表要靠 `*` 行拿到真实高度。
- 不要在 `AppColorPalette` 里改动槽位顺序或色值；顺序是色觉障碍分离度的保证。
- 不要在每秒刷新的路径上重复提取图标；`AppIconProvider` 已按路径缓存。
- **新增图标不要再用 `Segoe MDL2 Assets` 字符**；统一在 `Themes/Icons.xaml` 加 `StreamGeometry`，用 `<Path Data=”{StaticResource Icon.Xxx}” />` 引用。
- **`Icons.xaml` 必须在 `App.xaml` 合并字典里排在 `Theme.xaml` 之前**：`Theme.xaml` 模板里用 `{StaticResource Icon.*}` 是加载时解析，Icons 排后面会直接抛 `XamlParseException`（崩溃发生在打开含 ComboBox/Calendar 的页面时）。
- **Brush 属性不要写 `Fill=”None”`**：`None` 不是合法 Brush 字面量，BAML 反序列化时 `Brush.Parse` 抛 `FormatException`。要「无填充」写 `Fill=”Transparent”` 或 `Fill=”{x:Null}”`。
- **重写 ScrollBar 模板时，Track 必须是模板根元素**：外面套 `Border`/`Grid` 行都会让 `PART_Track` 拿不到完整可用空间，WPF 内部按 Track 实际尺寸分配 Decrease/Thumb/Increase 三段，会出现「内容滚到底、拇指却跑出可视区」的错位。视觉圆角应在 Thumb 自己的模板里加，不要包在外层容器上。
- **垂直 ScrollBar 自定义模板必须 `IsDirectionReversed="True"`**：默认 `False` 时滚轮方向正确，但**拖动 Thumb 方向会反**（WPF 默认模板的垂直 ScrollBar 就是 True）。改 ScrollBar 模板后必须做「滚轮向下/向上 + PageUp/PageDown 命令 + 拖动拇指」五种操作的完整自测。
- **ScrollBar 的翻页 RepeatButton 必须显式给透明模板**（`ScrollBar.PageButton`）：只设 `Background="Transparent"` 而不覆盖 `Template` 时，会套用 Aero2 默认 RepeatButton 模板，其 `IsMouseOver` 触发器把整段翻页区（轨道空白处）刷成经典浅蓝，表现为「鼠标移到滚动条空白处出现高亮」。模板用可命中测试的透明 `Border`、不带任何触发器，既去掉悬停高亮又保留点击翻页（`Command` 仍在 RepeatButton 上）。验证悬停类改动要用「拇指悬停会变亮」作阳性对照，证明鼠标跟踪确实生效，避免把「没触发悬停」误判成「高亮已移除」。
- **圆角只能用两档**：`Radius.Small`（5，小控件）与 `Radius.Medium`（10，卡片/对话框）；不要硬编码 7、4 等值。
- **字号统一走 `FontSize.*` 资源键**（Caption/Body/Subtitle/Title/PageTitle/Display）；不要硬编码 `FontSize=”21”` 这类散值。**例外**：倒计时面板剩余时间/状态字号须随面板宽度自适应（`TimerViewModel.UpdateFontSizes` 以 `FormattedText` 实测文本宽度反推 `PanelFontSize`/`PanelStatusFontSize`），固定资源键或估算字宽都会在窄面板排版变形。
- **隐式 TextBlock 样式全局 `TextWrapping=Wrap`**：单行语义的文本（如倒计时面板数字）必须显式 `NoWrap`，否则字号再小也可能折行；配套字号要用 `FormattedText` 实测字宽（估算 0.56em/数字 小于 Segoe UI SemiBold 实际字宽，60px 宽面板仍会折行），且 VM 测量字体须与 View 的 FontFamily/FontWeight 一致。
- **实跑 UI 自动化经验**：`PrintWindow(hwnd, hdc, 2)` 在非前台甚至锁屏时仍能截 WPF 窗口；PowerShell 驱动 UIA 前须 `SetProcessDPIAware()`，否则缩放屏上 `SetCursorPos` 坐标被虚拟化偏移；WPF ComboBox 的 ListItem UIA Name 是数据项 ToString（非 DisplayMemberPath 文本），按 Label 子串匹配或改用键盘选择；注入键鼠可能被前台应用（如 Unreal Editor）吞，点击/热键要重试；隐藏态主窗口 UIA 枚举不到，可再启动一个实例触发单实例唤醒呼出（第二实例自行退出）。

- **局部 DataGridRow 样式必须继承全局隐式样式**：需要 ToolTip 等局部 Setter 时使用 `BasedOn="{StaticResource {x:Type DataGridRow}}"`，否则会丢失全局 hover/IsSelected 反馈。单项命令对应的 DataGrid 显式使用 Single + FullRow。
- **危险操作必须通过可替换确认入口**：软件删除、时间记录删除/合并/清除当日统一走 `IConfirmationService`；只有明确确认才调用编辑服务，取消与关闭均不得改数据。
- **统计排行榜选择使用稳定软件 Id 协调**：`SelectedAppRow` 是当前选择，刷新后按 `ApplicationId` 重绑定并重算详情；切周期/日期立即清空，不能只保留可能过期的详情字符串。

- **BAT 停止脚本不得强杀应用**：`desktop.ps1` 只发送正常关闭请求；若应用按设置最小化到托盘，等待超时后提示从托盘退出，不得改用 `taskkill /F`。
- **Windows 清理与打包只操作仓库内受管路径**：删除前拒绝 reparse point；`package.ps1` 必须先在 `.runtime/staging/package-<id>/` 暂存并校验程序集版本、单文件形态与 zip 全项，再用 `Publish-PackageSet` 事务替换 `dist/dev` / `dist/release`，失败逆序回滚并保留暂存现场；不要在打包前删除旧产物。检测到运行实例时只列出进程并询问，输入 `Y` 才可先正常关闭、12 秒后再强制结束；`stop.bat` / `desktop.ps1` 仍不得强杀。

- **取进程路径不能只靠 `Process.MainModule`**：受保护或位数不匹配的进程会拒绝访问；统一使用 `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) + QueryFullProcessImageName` 回退，现有实现位于 `ProcessScanner.TryGetPath` 与 `ForegroundWindowMonitor.QueryPath`。
- **分类持久化值与显示文案分离**：`TrackedApp.Category` 保持中文稳定值；筛选下拉使用「稳定值 + 本地化标签」的 `OptionItem`，不要比较界面文案。
- **被 `CollectionView` 包装的集合必须回 Dispatcher 更新**：测试并行执行或后台回调中直接修改会抛跨线程异常；无 `Application` 的测试环境可跳过 UI 刷新。
- **DatePicker/Calendar 主题必须显式接线**：`DatePicker.CalendarStyle → Calendar.Style → CalendarItemStyle/CalendarDayButtonStyle/CalendarButtonStyle` 整条链不能缺；ComboBox 收起框显式设置 `VerticalContentAlignment="Center"`。
- **会话集合不是线程安全的公开 List**：后台心跳/前台事件在写，UI 与统计在读。读取一律用 `AppRuntime.Sessions`（深拷贝快照）或 `GetLiveSessionsSnapshot()`；修改一律经 `IActivitySessionService.EditSessionsAsync`/`ClearSessionsAsync`，不要在 UI 或 `SessionEditor` 里直接 `Add/Remove/Clear`。
- **进行中的会话不能手工编辑、删除或合并**：`SessionEditor` 会拒绝；时间记录页先判断 `EndedAtUtc` 并给出提示，否则会与状态机里的同一对象失去同步。
- **关闭窗口必须走完整退出链**：`MinimizeToTrayOnClose=false` 时由 `MainWindow.OnClosing` 转调 `App.ExitApplication(reason)`；直接让窗口关闭会绕过 `StopAsync` 和会话落盘。退出入口必须带原因字符串，便于日志归因。
- **恢复备份先停写**：`PrepareForRestoreAsync` 会停止协调器并以 `StopWithoutSavingAsync` 结束内存会话；导入后必须 `ExitWithoutSaving(reason)`，否则退出流程会用旧数据覆盖刚导入的文件。导入失败必须调用 `ResumeAfterRestoreFailureAsync` 续听。
- **备份只包含五份业务 JSON**：`JsonAppDataStore.DataFiles` 是创建与导入的共同白名单，不打包日志、`.bak` 或临时文件；新增持久化文件时须同时评估是否加入该列表。
- **进程扫描不要无条件跑**：只有 `ShouldScanRunningProcesses` 为真时才枚举进程；运行模式扫描不读取窗口标题，也不要恢复每次心跳都 `Process.GetProcesses()` 的旧实现。
- **概览页每秒只做增量更新**：`UpdateLiveDurations` 只改活跃卡时长和今日总计；完整统计、排行与时间线只在 `DataChanged`/页面激活时重算。不要在 1 秒定时器里克隆全部会话。
- **跨零点拆分会话必须覆盖所有完成路径**：正常窗口切换、空闲/锁屏状态会话、异常恢复都要调用 `SplitAcrossLocalMidnights`；只在 `CompleteAppSession` 里拆会漏掉恢复和状态会话。
- **短切换合并只能清理已结束的软件会话**：不要把 `ApplicationId is null` 的空闲/锁屏状态会话或手工记录当作临时会话删除。
- **`GetLastInputInfo.Time` 是 32 位 tick**：与 `GetTickCount64` 相减必须按 uint 取模（见 `IdleStateMonitor.CalculateElapsedMilliseconds`），否则系统运行超过约 49.7 天后会被误判为长期空闲。
- **托盘 NotifyIcon 与 UI 刷新必须回 Dispatcher**：快照事件来自后台线程，`TrayIconService.Update` 与各 ViewModel 的刷新都通过 `Dispatcher.BeginInvoke` 合并执行；不要改回同步 `Dispatcher.Invoke` 阻塞监听线程。
- **后台线程不得读 `Application.Current.MainWindow`**：该 getter 带 `VerifyAccess`，在监听线程调用即抛 `InvalidOperationException`；`OverviewViewModel.RequestRefresh` 只用 `ShouldSkipRefresh` 在 `dispatcher.CheckAccess()` 为真时探测可见性，后台事件一律排队、由回调在 UI 线程再判断（1.3.0 曾因此每秒刷 ERROR 且增量更新失效）。
- **倒计时结束动作必须回 UI 线程且检查退出态**：`CountdownTimerService.Finished` 来自后台线程，`App.OnCountdownFinished` 用 `Dispatcher.BeginInvoke` 并检查 `_isExiting`，避免退出瞬间弹桌布或锁屏。
- **倒计时面板位置必须夹取到虚拟屏幕内**：拖拽保存与首次显示都按 `SystemParameters.VirtualScreen*` 夹取，防止分辨率变化后面板跑出屏幕；持久化用 nullable double（null=默认位置），不要把 NaN 写进 JSON。
- **新增导航页必须三件齐备**：`MainWindow.xaml` 的 NavButton RadioButton（`Command=NavigateCommand` + `CommandParameter`）、`MainViewModel` 的页面属性与 `Navigate` 分支、`App.xaml` 的 VM→View DataTemplate，缺一即白页或导航失效。RadioButton 的 Command 只在 Click 触发（UIA `SelectionItemPattern.Select` 不导航），自动化核对要点其内部 Text 元素；同名文案可能同时出现在页面标题，取最靠左者。
- **倒计时面板宽度绑定放在根 Border，不要绑 Window.Width**：`SizeToContent` + `WindowStyle=None` + `AllowsTransparency` 组合下 Window 级 Width 绑定会被内容测量覆盖（实测窗口恒为默认宽、改设置不生效）；正确做法是根 Border 绑 `PanelWidth` + `SizeToContent="WidthAndHeight"`。初始位置用设置宽度计算，不要用 `ActualWidth`（Loaded 时 SizeToContent 布局可能尚未完成）。
- **面板/桌布逐层不透明度必须 `AllowsTransparency="True"` 且根背景 Transparent**：去掉 `Window.Opacity` 改用子层 `Opacity` 实现「文字/底板分离」时，若不开逐像素 alpha，子层 Opacity 会整窗合成失效；根 Border/GridLayout 背景设 Transparent，底板与文字各自一层绑 fraction。
- **旧配置迁移只能以「旧字段 > 0」判定，不能以「新字段 <= 0」判定**：旧 JSON 不含新字段，`System.Text.Json` 反序列化到新 `AppSettings` 时缺字段会落到**模型初始化器默认值**（如 `TimerDurationSeconds=1500`，非 0），用 `新字段<=0 && 旧字段>0` 会漏迁移、用户旧值被默认值覆盖。正确做法与不透明度迁移一致：`旧字段>0` 即迁移并清零旧字段（新配置旧字段恒 0，不会误迁移）。
