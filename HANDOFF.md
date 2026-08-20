# 接力进度 · app-usage-tracker

## 元数据

- 当前分支：`main`
- 状态日期：2026-08-20
- 构建命令：`sh manager.sh build`
- 测试命令：`sh manager.sh test`
- 启动命令：`sh manager.sh start`
- 文档检查：`sh check-docs.sh`

## 待办

无进行中任务。

## 本轮完成（2026-08-20）

1. 单实例守卫：`App.xaml.cs` 以命名 Mutex 保证单实例，重复启动时经命名 EventWaitHandle 唤醒首实例主窗口并退出。
2. 每日摘要时间可配置：`AppSettings.DailySummaryHour/Minute`（默认 18:00），设置页「通知与隐私」即时生效，`UsageNotificationService` 按配置时间触发；新增 7 个阈值单元测试。
3. 统计分析周期跨 tab 保持：周期 RadioButton 改为 `EnumEqualsConverter` 双向绑定 `SelectedPeriod`，去掉硬编码 IsChecked 与 `SelectPeriodCommand`。

验证：build 通过（0 警告）、test 78 通过、check-docs 通过、单实例实跑（二次启动进程数保持 1）。
