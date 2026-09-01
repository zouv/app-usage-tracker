# 接力进度 · app-usage-tracker

## 元数据

- 当前分支：`main`
- 状态日期：2026-09-01
- 构建命令：`sh manager.sh build`
- 测试命令：`sh manager.sh test`
- 启动命令：`sh manager.sh start`
- 文档检查：`sh check-docs.sh`

## 待办

无进行中任务。

## 注意事项

- 中英文文案的唯一来源是 `src/AppUsageTracker/Strings/Strings.Chinese.xml` 与 `Strings.English.xml`（嵌入资源），键必须一一对应，有本地化测试兜底。**文件名不得带语言标签**（如 `Strings.zh-CN.xml`）：MSBuild 会把 `*.zh-CN.*` 当作卫星资源，既不嵌入主程序集也不会报错，代码侧文案会回退成键名。
- 软件分类以中文值持久化（`TrackedApp.Category`），英文界面只翻译显示名，不要改动存储值；分类/状态的筛选下拉用「稳定值 + 本地化标签」的 `OptionItem` 绑定，不要直接比较界面文案。
- 本地化开关会触发全局 `LocalizationService.LanguageChanged`，新增的 ViewModel/服务如需动态文案记得订阅重算；托盘菜单等 WinForms 控件必须在 UI 线程更新（事件本身在 UI 线程同步触发）。**凡是改了被 `CollectionView` 包着的集合（如 `AppsViewModel.Apps`）必须回 Dispatcher 线程**——单元测试里 xUnit 会用不同线程跑不同测试类，跨线程改 CollectionView 会抛 `NotSupportedException`，`ci.bat` 的 Release 测试就曾因此失败；无 `Application` 时直接跳过该 UI 刷新。
- **日历与下拉框的主题必须显式接线，隐式 Style 不生效**：`DatePicker` 在代码里把内部 `Calendar` 的 Style 绑定到 `DatePicker.CalendarStyle`，`Calendar` 又把 `CalendarDayButtonStyle`/`CalendarButtonStyle` 传给日/月按钮（`CalendarItem` 代码里 `SetBinding(CalendarDayButton.StyleProperty, …)`）。所以只写 `<Style TargetType="CalendarDayButton">` 不会被日按钮采用，必须 `DatePicker.CalendarStyle → Calendar.Style → CalendarItemStyle/CalendarDayButtonStyle/CalendarButtonStyle` 一整条链接上；`ComboBox` 收起框文字垂直居中要显式设 `VerticalContentAlignment="Center"`（默认是 Top，文字会偏上）。

## 本轮完成（2026-08-21）

1. 中英文界面切换：`AppSettings.Language`（默认中文）+ 设置页「语言」卡片即时生效；`LocalizationService` 整体替换合并字典第 1 位的字符串字典，XAML 用 `{DynamicResource Loc.*}`、代码用 `T()`；覆盖五个页面、侧边栏、托盘、通知、对话框、时长单位、日期格式与分类/状态/统计模式显示，并新增字典键一致性与时长单位本地化测试。
2. 图表纵轴单位由字符串「小时/分钟」改为 `ChartValueUnit` 枚举（原实现靠比较中文单位字符串换算轴值），图例与悬浮提示文案同步本地化。
