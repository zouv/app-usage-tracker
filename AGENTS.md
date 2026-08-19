# Agent 指南 · app-usage-tracker

> 本文件适用于 `app-usage-tracker` 项目。每次开工先读本文件、`ARCHITECTURE.md` 和 `HANDOFF.md`。

## 1. 工作方式

1. 先看 `ARCHITECTURE.md` 的作用域路由，只读取当前任务相关文件。
2. 开始常规功能前，在 `HANDOFF.md` 写明目标、范围、完成定义和下一步。
3. 修改代码结构、接口或关键链路时同步更新 `ARCHITECTURE.md`。
4. 完成用户可感知功能时更新 `CHANGELOG.md`。
5. 会话结束或中断前更新 `HANDOFF.md`，确保下一次可以直接续作。

## 2. 技术约束

- 技术栈：C#、.NET 8、WPF、CommunityToolkit.Mvvm 8.3.2。
- 目标框架：`net8.0-windows`。
- 视图使用 XAML；业务逻辑放在 ViewModel 和 Service。
- 系统托盘使用 WinForms `NotifyIcon`，不额外引入托盘库。
- 配置和数据使用 `System.Text.Json`，默认保存到 `%AppData%\AppUsageTracker\`。
- 前台窗口监听通过 Win32 P/Invoke 封装，调用方不得直接散落原生 API。
- 后台心跳使用 `System.Timers.Timer`；UI 刷新使用 `DispatcherTimer`。
- 单元测试使用 xUnit，系统 API 必须通过接口抽象后使用假实现测试。
- MVP 阶段不引入 Entity Framework Core、SQLite、第三方进程库或重型图表库。
- UI 图表优先使用 WPF 原生控件、数据模板和绘图元素。

## 3. 统计与数据铁律

- 默认只累计“已配置软件 + 前台激活 + 未锁屏/休眠 + 未空闲”的有效时长。
- 同一设备同一时刻最多只能存在一个有效前台会话。
- 会话跨越零点时必须按自然日拆分。
- 使用 UTC 保存时间戳，使用单调时间源计算持续时间。
- 原始会话是事实数据源，汇总数据必须可以重建。
- 写入数据时先写临时文件，再原子替换正式文件。
- 读取损坏文件时回退备份或默认值，不得阻断应用启动。
- 默认不记录窗口标题、文档名、网址等敏感内容。
- 锁屏、休眠、手动暂停和隐私模式期间不得累计有效时长。

## 4. 构建与验证

```bash
sh manager.sh build
sh manager.sh test
sh check-docs.sh
```

UI 或交互改动还必须：

```bash
sh manager.sh start
```

并人工确认：

- 窗口无重叠、裁切和异常滚动。
- 导航、按钮、输入控件和状态切换正常。
- 关闭窗口和托盘行为符合设置。

发布验证：

```bash
sh manager.sh pack
```

新版本发布走 `/app-release` skill：AI 判定版本号、预检、改写 CHANGELOG，调用 `sh manager.sh release <version>` 升级版本号并打包，复核后提交、打 tag、推送并创建 GitHub Release。

## 5. 项目文档

| 文件 | 用途 |
| --- | --- |
| `ARCHITECTURE.md` | 当前代码结构、任务路由和关键链路 |
| `HANDOFF.md` | 当前进行中的任务、验证状态和下一步 |
| `CHANGELOG.md` | 已完成版本和用户可感知变化 |
| `docs/00-目录.md` | 文档入口 |
| `docs/01-技术栈说明.md` | 技术选型和依赖 |
| `docs/02-使用说明.md` | 构建、测试、运行和发布 |
| `docs/design/software-usage-duration-design.md` | 产品与界面设计 |

## 6. 代码规范

- 启用 Nullable，不使用无理由的空值抑制。
- 公共类型和复杂业务规则使用简短中文 XML 注释。
- 不在 View Code-behind 中实现业务规则。
- 不吞掉无法恢复的异常；可恢复的系统探测异常应记录调试日志并降级。
- 新增依赖前先确认标准库或现有依赖不能满足需求。
- 编辑范围保持聚焦，不重构与当前任务无关的代码。

## 7. 完成定义

常规或结构性改动必须同时满足：

1. 项目可以构建。
2. 相关测试通过。
3. UI 改动完成实跑检查。
4. `ARCHITECTURE.md` 与代码一致。
5. `CHANGELOG.md` 和 `HANDOFF.md` 状态正确。
6. `sh check-docs.sh` 通过。

## 8. 其他

- 使用中文沟通。
