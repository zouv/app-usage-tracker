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
| 构建发布 | `manager.sh`、`*.csproj`、`AppUsageTracker.sln` | 业务服务 |
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
| `src/AppUsageTracker/ViewModels/` | 已有 | 五个页面和应用壳的状态与命令 |
| `src/AppUsageTracker/Views/` | 已有 | 概览、软件管理、统计、时间记录和设置 |
| `src/AppUsageTracker/Controls/ZoomableBarChart.cs` | 已有 | 自绘可缩放堆叠柱状图，滚轮缩放、拖拽平移、悬停高亮 |
| `src/AppUsageTracker/Controls/ChartLegend.cs` | 已有 | 图表图例，与柱状图双向联动高亮 |
| `src/AppUsageTracker/ViewModels/ChartLayout.cs` | 已有 | 缩放、平移、槽位合并的纯换算逻辑 |
| `src/AppUsageTracker/ViewModels/ChartBuilder.cs` | 已有 | 把会话与统计结果组装成图表数据 |
| `src/AppUsageTracker/ViewModels/ChartData.cs` | 已有 | 图表系列、槽位和数据集契约 |
| `src/AppUsageTracker/Services/AppColorPalette.cs` | 已有 | 8 槽位配色，按软件自动分配并随主题解析 |
| `src/AppUsageTracker/Services/AppIconProvider.cs` | 已有 | 软件图标解析与缓存 |
| `src/AppUsageTracker/Services/GlobalHotkeyService.cs` | 已有 | 全局快捷键注册到主窗口句柄并分发 WM_HOTKEY，托盘隐藏后仍可响应 |
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
| `manager.sh` | 已有 | build/start/test/icon/pack/clean/release 统一入口 |

## 2. 按「我要做什么」反查代码位置（任务 → 入口）

> 只登记常见改动的入口函数或类型锚点，不是全量函数表。按名称搜索；搜不到时以代码为准，并在任务收尾时订正本表。

| 我要改的东西 | 位置 | 关键函数 / 锚点 |
| --- | --- | --- |
| 应用启动与初始化 | `App.xaml.cs`、`Services/AppRuntime.cs` | `App.OnStartup`、`AppRuntime.InitializeAsync` |
| 正常退出与会话收尾 | `App.xaml.cs`、`Services/ActivitySessionService.cs` | `App.ExitApplication`、`ActivitySessionService.StopAsync` |
| 主窗口导航与页面承载 | `MainWindow.xaml`、`ViewModels/MainViewModel.cs` | 页面导航属性与命令 |
| 前台窗口监听 | `Services/ForegroundWindowMonitor.cs`、`ApplicationMatcher.cs` | WinEventHook 封装、软件匹配入口 |
| Running 模式进程扫描 | `Services/ProcessScanner.cs`、`TrackingCoordinator.cs` | `EnumerateRunningProcesses`、`MatchRunningProcess`、`HandleRunningProcessesAsync` |
| 活动会话状态机与恢复 | `Services/ActivitySessionService.cs` | `HandleRunningProcessesAsync`、`RecoverOpenSessions`、`StopAsync` |
| 数据加载与原子保存 | `Services/JsonAppDataStore.cs` | `LoadAsync`、`WriteAtomicAsync` |
| 统计图表数据组装 | `ViewModels/ChartBuilder.cs`、`StatisticsViewModel.cs` | `BuildDaily`、`BuildWeekly`、`BuildByApp` |
| 柱状图缩放与绘制 | `Controls/ZoomableBarChart.cs`、`ViewModels/ChartLayout.cs` | `OnRender`、缩放/平移换算 |
| 界面语言与动态文案 | `Services/LocalizationService.cs`、`Strings/` | `Apply`、`T`、`LanguageChanged` |
| 明暗主题与资源字典 | `Services/ThemeService.cs`、`App.xaml`、`Themes/` | 调色板字典整体替换 |
| 全局快捷键呼出 / 隐藏 | `Services/GlobalHotkeyService.cs`、`App.xaml.cs` | WM_HOTKEY 分发、`App.ToggleMainWindow` |
| 托盘与通知 | `Services/TrayIconService.cs`、`UsageNotificationService.cs` | 托盘菜单、连续使用提醒、每日摘要 |
| 会话编辑与导出 | `Services/SessionEditor.cs`、`CsvExportService.cs` | 补录、修改、合并、CSV 导出 |
| 构建、发布与清理 | `manager.sh`、根目录 `*.bat` | `cmd_build`、`cmd_test`、`cmd_pack`、`cmd_release`、`cmd_clean` |

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
TrackingCoordinator.HeartbeatTimer
 -> IProcessScanner.EnumerateRunningProcesses()
 -> ApplicationMatcher.MatchRunningProcess()（返回所有命中 TrackingMode.Running 的软件，忽略窗口标题规则）
 -> ActivitySessionService.HandleRunningProcessesAsync()
 -> 前台命中与运行模式软件并行累计：同一软件只计一次，多软件各自独立会话
```

多软件并发累计：`ActivitySessionService` 按软件 Id 维护多条进行中的 Active 会话（`_appSessions` 字典），全局状态会话（空闲/锁屏/休眠/暂停/隐私）单独一条。前台命中优先作为快照主显示项，与后台运行软件同时各自累计；锁屏/休眠/暂停/隐私一次性结束全部软件会话。快照 `TrackingSnapshot.ActiveApps` 携带全部活跃软件（含来源模式），`CurrentApp/CurrentSession` 保留为主显示项（供托盘/通知/添加软件等单值消费）。

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
 -> MainWindow / TrayIconService

App.ExitApplication
 -> ActivitySessionService.StopAsync
 -> 保存设置 / 软件 / 会话 / 修正
 -> 释放托盘、系统监听与单实例互斥体
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
| 明暗调色板 | `Themes/Palette.Light.xaml`、`Palette.Dark.xaml` | 两文件资源键一一对应，由 `ThemeService` 整体替换 |
| 矢量图标 | `Themes/Icons.xaml` 的 `Icon.*` | 统一 StreamGeometry，通过 Path 引用，不新增 MDL2 字符图标 |
| 本地化文案 | `Strings/Strings.Chinese.xml`、`Strings.English.xml` | 键一一对应；XAML 用 `Loc.*`，代码用 `LocalizationService.T` |
| 图表与软件配色 | `Controls/`、`Services/AppColorPalette.cs` | 自绘几何与固定 8 槽位，不为大量柱段创建可视元素 |

## 6. 运维 / 脚本

> 运维任务只读本节、`manager.sh`、相关 BAT/PowerShell 与 `docs/02-使用说明.md`，无需进入业务代码。具体操作以使用说明为准。

| 入口 | 职责 |
| --- | --- |
| `manager.sh` | build/start/test/icon/pack/release/clean/help 统一入口；版本号写入主项目 csproj |
| `build.bat`、`test.bat`、`ci.bat` | Windows 构建与测试包装；`ci.bat` 禁用 MSBuild/Roslyn 常驻服务器 |
| `publish.bat` | 单文件发布与产物回退检查，保留 GenerateBundle、相对输出路径等项目专用处理 |
| `clean.bat` | Windows 清理入口 |
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
- **圆角只能用两档**：`Radius.Small`（5，小控件）与 `Radius.Medium`（10，卡片/对话框）；不要硬编码 7、4 等值。
- **字号统一走 `FontSize.*` 资源键**（Caption/Body/Subtitle/Title/PageTitle/Display）；不要硬编码 `FontSize=”21”` 这类散值。

- **取进程路径不能只靠 `Process.MainModule`**：受保护或位数不匹配的进程会拒绝访问；统一使用 `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) + QueryFullProcessImageName` 回退，现有实现位于 `ProcessScanner.TryGetPath` 与 `ForegroundWindowMonitor.QueryPath`。
- **分类持久化值与显示文案分离**：`TrackedApp.Category` 保持中文稳定值；筛选下拉使用「稳定值 + 本地化标签」的 `OptionItem`，不要比较界面文案。
- **被 `CollectionView` 包装的集合必须回 Dispatcher 更新**：测试并行执行或后台回调中直接修改会抛跨线程异常；无 `Application` 的测试环境可跳过 UI 刷新。
- **DatePicker/Calendar 主题必须显式接线**：`DatePicker.CalendarStyle → Calendar.Style → CalendarItemStyle/CalendarDayButtonStyle/CalendarButtonStyle` 整条链不能缺；ComboBox 收起框显式设置 `VerticalContentAlignment="Center"`。
