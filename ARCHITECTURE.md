# 代码链路图谱 · app-usage-tracker

> 本文档描述当前工程结构、关键链路与任务作用域。

## 0. 一句话架构

.NET 8 WPF 单主窗口应用，通过 MVVM 组织界面，以 Win32 前台窗口事件驱动活动会话，使用 JSON 本地持久化原始会话和统计数据。

```text
App
 └─ MainWindow
     └─ MainViewModel
         ├─ OverviewViewModel
         ├─ AppsViewModel
         ├─ StatisticsViewModel
         ├─ TimelineViewModel
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
| 软件配色与图标 | `Services/AppColorPalette.cs`、`Services/AppIconProvider.cs` | 统计聚合 |
| 柱状图与缩放 | `Controls/ZoomableBarChart.cs`、`Controls/ChartLegend.cs`、`ViewModels/Chart*.cs` | 持久化实现 |
| 时间线排版 | `ViewModels/ChartBuilder.cs`、`ViewModels/TimelineLayout.cs`、`Views/OverviewView.xaml` | 持久化实现 |
| 图标与资源 | `tools/generate-icon.ps1`、`Assets/`、`*.csproj` | 业务服务 |
| 前台监听 | `Services/ForegroundWindowMonitor*`、`Services/ApplicationMatcher*` | 统计页视觉 |
| 空闲、锁屏、休眠 | `Services/IdleStateMonitor*`、`Services/SystemSessionMonitor*` | 软件管理界面 |
| 会话与心跳 | `Services/ActivitySessionService*`、`Models/ActivitySession*` | 托盘视觉 |
| 数据持久化 | `Services/JsonDataStore*`、`Models/` | XAML 页面 |
| 统计查询 | `Services/StatisticsService*`、统计模型和测试 | Win32 封装 |
| 软件管理 | `Views/AppsView*`、`ViewModels/AppsViewModel*`、`TrackedApp*` | 时间线编辑 |
| 构建发布 | `manager.sh`、`*.csproj`、`AppUsageTracker.sln` | 业务服务 |
| 产品范围 | `docs/design/software-usage-duration-design.md` | 生成目录 |

## 1. 当前文件职责

| 路径 | 状态 | 职责 |
| --- | --- | --- |
| `AppUsageTracker.sln` | 已有 | 主项目和测试项目解决方案 |
| `src/AppUsageTracker/App.xaml` | 已有 | WPF 应用入口和全局资源 |
| `src/AppUsageTracker/MainWindow.xaml` | 已有 | 单主窗口、左侧导航和页面承载 |
| `src/AppUsageTracker/AppUsageTracker.csproj` | 已有 | WPF、WinForms 托盘和 MVVM 依赖配置 |
| `tests/AppUsageTracker.Tests/` | 已有 | xUnit 测试项目 |
| `src/AppUsageTracker/Models/` | 已有 | 软件、规则、会话、设置和统计模型 |
| `src/AppUsageTracker/Services/` | 已有 | 监听、状态机、持久化、统计、托盘和导出 |
| `src/AppUsageTracker/ViewModels/` | 已有 | 五个页面和应用壳的状态与命令 |
| `src/AppUsageTracker/Views/` | 已有 | 概览、软件管理、统计、时间记录和设置 |
| `src/AppUsageTracker/Controls/ZoomableBarChart.cs` | 已有 | 自绘可缩放堆叠柱状图，滚轮缩放、拖拽平移、悬停高亮 |
| `src/AppUsageTracker/Controls/ChartLegend.cs` | 已有 | 图表图例，与柱状图双向联动高亮 |
| `src/AppUsageTracker/ViewModels/ChartLayout.cs` | 已有 | 缩放、平移、槽位合并的纯换算逻辑 |
| `src/AppUsageTracker/ViewModels/ChartBuilder.cs` | 已有 | 把会话与统计结果组装成图表数据 |
| `src/AppUsageTracker/ViewModels/ChartData.cs` | 已有 | 图表系列、槽位和数据集契约 |
| `src/AppUsageTracker/Services/AppColorPalette.cs` | 已有 | 8 槽位配色，按软件自动分配并随主题解析 |
| `src/AppUsageTracker/Services/AppIconProvider.cs` | 已有 | 软件图标解析与缓存 |
| `src/AppUsageTracker/Services/GlobalHotkeyService.cs` | 新增 | 全局快捷键注册到主窗口句柄并分发 WM_HOTKEY，托盘隐藏后仍可响应 |
| `src/AppUsageTracker/Models/HotkeyDefinition.cs` | 新增 | 快捷键字符串与「修饰符 + 虚拟键码」的双向转换 |
| `src/AppUsageTracker/Themes/Theme.xaml` | 已有 | 公共控件样式，颜色一律 DynamicResource |
| `src/AppUsageTracker/Themes/Palette.Light.xaml` | 已有 | 浅色调色板，可被整体替换 |
| `src/AppUsageTracker/Themes/Palette.Dark.xaml` | 已有 | 深色调色板，键与浅色一一对应 |
| `src/AppUsageTracker/Assets/app.ico` | 已有 | 应用图标，用于 exe、窗口、侧边栏和托盘 |
| `tools/generate-icon.ps1` | 已有 | 可重现地生成多尺寸图标 |
| `tools/capture-ui.ps1` | 已有 | 启动应用、切页并截图，供 UI 实跑核对 |
| `docs/design/` | 已有 | 产品、技术和界面设计 |
| `manager.sh` | 已有 | build/start/test/icon/pack/clean/release 统一入口 |

## 2. 模块位置

| 模块 | 路径 | 主要职责 |
| --- | --- | --- |
| 应用壳 | `MainWindow.xaml`、`MainViewModel.cs` | 左侧导航和页面切换 |
| 主题 | `Services/ThemeService.cs`、`Themes/Palette.*.xaml` | 整体替换调色板字典实现明暗切换 |
| 时间线排版 | `ViewModels/ChartBuilder.cs`、`ViewModels/TimelineLayout.cs` | 时间窗对齐、分箱摊分和槽位换算 |
| 图表控件 | `Controls/ZoomableBarChart.cs`、`Controls/ChartLegend.cs` | 自绘柱状图、缩放平移和图例联动 |
| 软件配色 | `Services/AppColorPalette.cs` | 8 槽位自动分配，随明暗主题解析显示色 |
| 软件图标 | `Services/AppIconProvider.cs` | Base64 优先、回退 exe 提取并缓存 |
| 概览 | `Views/OverviewView.xaml` | 当前软件、今日摘要、时间线和排行 |
| 软件管理 | `Views/AppsView.xaml` | 软件条目增删改查和扫描 |
| 统计分析 | `Views/StatisticsView.xaml` | 日、周、月、年和累计统计 |
| 时间记录 | `Views/TimelineView.xaml` | 原始会话查看和修正 |
| 设置 | `Views/SettingsView.xaml` | 启动、统计、通知和隐私选项 |
| 前台监听 | `Services/ForegroundWindowMonitor.cs` | Win32 前台窗口变化 |
| 系统状态 | `Services/IdleStateMonitor.cs`、`SystemSessionMonitor.cs` | 空闲、锁屏和休眠 |
| 软件匹配 | `Services/ApplicationMatcher.cs` | 路径、进程名和标题匹配 |
| 会话服务 | `Services/ActivitySessionService.cs` | 开始、结束、心跳和恢复 |
| 持久化 | `Services/JsonDataStore.cs` | 原子保存、备份和恢复 |
| 统计服务 | `Services/StatisticsService.cs` | 多周期聚合查询 |
| 托盘 | `Services/TrayIconService.cs` | 后台常驻和快捷菜单 |
| 全局快捷键 | `Services/GlobalHotkeyService.cs`、`Models/HotkeyDefinition.cs` | 系统级快捷键呼出 / 隐藏主窗口 |
| 通知 | `Services/UsageNotificationService.cs` | 连续使用提醒和每日摘要 |
| 数据编辑 | `Services/SessionEditor.cs` | 会话补录、修改、删除和合并 |
| 导出备份 | `CsvExportService.cs`、`JsonAppDataStore.cs` | CSV、ZIP 备份和恢复 |

## 3. 关键链路

### 3.1 前台活动会话

```text
WinEventHook
 -> ForegroundWindowMonitor
 -> ApplicationMatcher
 -> ActivitySessionService.EndCurrent()
 -> ActivitySessionService.StartMatched()
 -> JsonDataStore
```

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

### 3.5 应用启动与退出

```text
App.OnStartup
 -> JsonAppDataStore.LoadAsync
 -> AppRuntime.InitializeAsync
 -> ActivitySessionService.RecoverOpenSessions
 -> TrackingCoordinator.Start
 -> MainWindow / TrayIconService

App.ExitApplication
 -> ActivitySessionService.StopAsync
 -> 保存设置 / 软件 / 会话 / 修正
 -> 释放托盘与系统监听
```

## 4. 数据契约

### ActivitySession

| 字段 | 规则 |
| --- | --- |
| `ApplicationId` | 对应已配置软件 |
| `StartedAtUtc` | UTC 开始时间 |
| `EndedAtUtc` | UTC 结束时间，可在活动中为空 |
| `DurationSeconds` | 使用单调时间计算 |
| `ActivityType` | Effective、Foreground、Running 等 |
| `EndReason` | WindowChanged、Idle、Locked、Paused 等 |
| `LastHeartbeatAtUtc` | 异常恢复上界 |
| `IsManual` | 是否由用户补录或修改 |

## 5. 高频约束

- 不要把“进程存在”当作默认有效使用。
- 不要直接使用系统时钟差值计算持续时间。
- 不要在 WinEvent 回调中执行磁盘 IO 或复杂查询。
- 不要在 View Code-behind 中维护监听状态。
- 不要直接覆盖正式 JSON 文件。
- 不要让多个 ViewModel 分别创建监听服务实例。
- 不要在系统事件回调线程中直接更新 WPF 控件。
- 不要把窗口标题写入持久化会话；标题只用于内存匹配。
- 不要用 `StaticResource` 引用 `Brush.*`；主题切换依赖 `DynamicResource` 才能即时重绘。
- 不要改动调色板字典在 `App.xaml` 合并字典中的第 0 位；`ThemeService` 按下标整体替换。
- 不要给柱状图的柱子创建可视元素；成百上千个柱段必须走 `OnRender` 直接出几何。
- 不要把概览页和统计页整体套进 `ScrollViewer`；图表要靠 `*` 行拿到真实高度。
- 不要在 `AppColorPalette` 里改动槽位顺序或色值；顺序是色觉障碍分离度的保证。
- 不要在每秒刷新的路径上重复提取图标；`AppIconProvider` 已按路径缓存。

## 6. 维护要求

新增、删除或重命名模块后，更新本文档的作用域路由、文件职责和关键链路。
