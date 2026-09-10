# WorkMate

WorkMate 是一个托盘优先的 Windows 常驻小工具。V0.2 基线已实现：

- 单 EXE、自包含发布，目标电脑无需安装 .NET Runtime。
- 首次运行默认写入当前用户的 `HKCU Run` 自启动项，无需管理员权限。
- `WorkMate.exe` 主动启动时显示 Dashboard；`WorkMate.exe --startup` 开机启动时仅进入托盘。
- 开机启动后随机等待 5～15 秒再播放规则式中文问候。
- 每个自然日最多尝试一次开机问候，崩溃或重复启动不会连续播报。
- 命名 Mutex 保证单实例；再次主动双击会唤醒已有实例并打开 Dashboard。
- 关闭主窗口只隐藏到托盘，真正退出必须使用托盘菜单的“退出”。
- 工作日按当前班表的下午下班时间，默认提前 30 分钟提醒整理工位和打扫卫生。

## 发布

开发电脑需要 .NET 10 SDK，最终用户不需要安装 SDK 或 Runtime。选择 .NET 10 LTS 是因为其支持期到 2028 年 11 月；不再以将在 2026 年 11 月结束支持的 .NET 8 作为新项目基线。

```powershell
.\build\Publish.ps1
```

发布结果：

```text
artifacts\win-x64\WorkMate.exe
```

发布脚本会检查输出目录是否确实只有一个 `WorkMate.exe`。当前发布档案采用：

| 选项 | V0.1 | 原因 |
| --- | --- | --- |
| `SelfContained` | `true` | 目标电脑无需 .NET Runtime |
| `PublishSingleFile` | `true` | 交付物只有一个 EXE |
| `IncludeNativeLibrariesForSelfExtract` | `true` | 将 SQLite 与 .NET 原生库放进单文件包，运行时按官方机制解压 |
| `PublishTrimmed` | `false` | WPF/WinForms 托盘组件不适合 trimming，稳定优先 |
| `PublishReadyToRun` | `false` | 小型常驻应用收益有限，还会增大文件和工作集；待实测冷启动后再决定 |
| `EnableCompressionInSingleFile` | `true` | 降低交付体积，启动只发生在登录或手动打开时 |

## 图标资源

正式源 PNG 位于 `Assets\Icons\Source`。`build\PrepareIcons.py` 会在发布前从仓库根目录的 `Assets` 同步源文件，生成：

- `Assets\Icons\WorkMate.ico`：包含 16、20、24、32、48、64、128、256px 八种尺寸。
- `Assets\Icons\Generated`：每种图标的 16、20、24、32、64、96、128px 优化 PNG。

1254px 原图不会嵌入发布 EXE。运行时由 `IconService` 懒加载并缓存已经优化的小图；托盘根据系统 DPI 选择 16/20/24/32px，Dashboard 使用 96px。所有 `System.Drawing.Icon` 均由服务统一释放，PNG 转换产生的原生图标句柄会立即调用 `DestroyIcon`，避免 GDI Handle 泄漏。

`IconStateResolver` 使用固定优先级：正在显示的提醒 > 加班 > 实验台/会议模式 > 班表状态 > 默认图标。图标切换只在状态改变时发生，不使用轮询或实时缩放。

## 数据库选型

V0.1 使用 SQLite，数据库位于：

```text
%LOCALAPPDATA%\WorkMate\workmate.db
```

| 评估角度 | SQLite | LiteDB | V0.1 判断 |
| --- | --- | --- | --- |
| 单 EXE | 需要嵌入并提取原生库 | Pure .NET，流程更直接 | LiteDB 略优，但 SQLite 已验证单 EXE 输出 |
| 稳定性 | 生态成熟，事务与恢复机制经过长期验证 | 个人工具场景成熟，但生态与诊断工具较小 | SQLite 优先 |
| 数据量 | 适合长期增长的时间序列明细和聚合 | 中小数据量足够 | SQLite 更有余量 |
| 查询能力 | SQL、索引、窗口与聚合查询完整 | 文档查询简单直接 | Dashboard 统计明显偏向 SQLite |
| 未来 Dashboard | 可直接完成时间区间、应用排行和趋势聚合 | 复杂统计通常需要更多应用层代码 | SQLite 优先 |
| 长期使用 | 迁移、备份、修复和第三方分析工具丰富 | 单文件文档库易部署 | SQLite 优先 |

选择 SQLite 而不是 LiteDB 的主要原因是：长期活动数据、时间区间聚合、后续 Dashboard 统计和迁移都更适合关系模型与 SQL。`Microsoft.Data.Sqlite` 默认携带一致版本的原生 SQLite；单文件发布通过 `IncludeNativeLibrariesForSelfExtract=true` 将原生库嵌入 `WorkMate.exe`。这仍是“单 EXE 交付”，但首次启动会由 .NET 单文件宿主在当前用户临时目录中提取原生组件。

数据库启用 WAL、`synchronous=NORMAL` 和连接池。事件采集层应先在内存聚合，再以批次或事务写入，禁止按每次键鼠事件直接写库。

## 运行行为

首次启动会创建以下用户级注册表项：

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
Name:  WorkMate
Value: "C:\...\WorkMate.exe" --startup
```

设置页取消“开机自动启动”时会删除该项。若 EXE 移动到新位置，下一次主动启动会更新注册表中的路径。

应用数据、日志和 SQLite 的 WAL 辅助文件属于运行数据，不属于发布包。它们保存在 `%LOCALAPPDATA%\WorkMate`，不会污染 EXE 所在目录。

## 班表与工作状态

班表由 `ScheduleEngine` 统一加载，设置以 JSON 形式保存在 SQLite 的 `ScheduleSettings` 键中。V0.1 默认值集中定义在 `src\WorkMate\Services\ScheduleDefaults.cs`：

| Profile | 上午 | 下午 |
| --- | --- | --- |
| 夏令时 | 08:15～11:15 | 12:30～17:00 |
| 冬令时 | 08:15～11:15 | 12:00～16:45 |

默认自动切换，05-01～09-30（含首尾）使用夏令时，其余日期使用冬令时。设置也支持跨年范围，例如 11-01～03-31。周六、周日默认是 `OFF_WORK`。

状态边界采用左闭右开规则：

- 上午上班前：`BEFORE_WORK`。
- `[上午上班, 上午下班)`：`WORKING`。
- `[上午下班, 下午上班)`：`LUNCH`。
- `[下午上班, 下午下班)`：`WORKING`。
- 下午下班后及周末：`OFF_WORK`。

保存设置后会立即持久化并热重载 ScheduleEngine。ScheduleEngine 只等待下一个班表边界或午夜；ReminderEngine 使用低频检查处理班表提醒的短暂到期窗口。普通工作提醒仅在 `WORKING` 或用户明确进入 `OVERTIME` 时启用，并受 AFK 与活动模式规则约束。真实工作时间由 `ActivitySnapshotService` 结合 Schedule、Idle/AFK、加班和手动模式按互斥时间片累计，因此自动排除午休、下班后和周末。

开机问候在延迟结束后读取 ScheduleEngine 的实时状态，分别使用上班前、工作中、午休、下班后或周末模板，不再按固定小时粗略判断。

## 下班前打扫卫生提醒

提醒时间由 `ReminderEngine` 根据当前有效的 `ScheduleProfile` 动态计算：

```text
CleaningReminderTime = AfternoonWorkEndTime - CleaningReminderAdvanceMinutes
```

默认开启并提前 30 分钟。夏令时默认在 17:00 提醒，冬令时默认在 16:30 提醒；修改班表或提前分钟数并保存后，单次计时器会立即重新安排，无需重启。提醒不在 UI 中计算，也不写死具体时刻。

仅工作日且提醒时刻处于 `WORKING` 状态、没有暂停全部提醒时触发。程序在提醒时间后启动，或电脑睡眠后错过超过 2 分钟时，不会补播过期提醒。正常下班时间是唯一计算锚点；即使以后增加加班模式，也不会在加班阶段再次触发。

提醒同时使用中文 TTS 和统一的 WorkMate 轻提示窗口。轻提示提供“已完成”“10 分钟后再提醒”“今天跳过”，每天最多延迟两次。Dashboard 和托盘都可直接标记“打扫卫生 ✓”。

设置保存在 SQLite 的 `CleaningReminderSettings` 键中；每日触发、完成、跳过、延迟次数和下次时间统一记录在 `reminder_history` 表，以 `(reminder_type, reminder_date)` 为唯一键，确保同一天重启后不会重复首轮提醒。

## 统一提醒体验

喝水、站立、午休前、午休开始、下午上班、下班、打扫卫生和自动加班询问都通过 `ReminderPresentationService` 排队。一次只显示一个右下角轻提示，Schedule 关键提醒优先于打扫卫生、站立和喝水；普通提醒之间默认至少间隔 3 分钟。Lock、Sleep 或排队期间过期的提醒不会在恢复后集中补播。

喝水间隔、站立连续工作时长、各自语音/弹窗、午休节点、下班语音/弹窗，以及全局 TTS 音量和语速都可在设置页修改并立即生效。LAB 默认保留喝水但关闭站立；MEETING 的普通提醒只显示轻提示而不播放语音；OVERTIME 恢复喝水和站立提醒。

提醒历史继续写入 `reminder_history`，并包含触发时间、实际显示时间、用户操作、完成/延迟/关闭状态。展示架构和队列规则见 `docs\REMINDER_PRESENTATION.md`。

## V0.2 活动统计

- 键盘和鼠标使用低级 Hook，仅在内存累计次数与移动距离，不保存具体按键、文字、点击坐标或轨迹。
- Idle / AFK 使用 `GetLastInputInfo`，默认 3 分钟为 IDLE、5 分钟为 AFK。
- 前台应用使用 `EVENT_SYSTEM_FOREGROUND`，只有进程发生变化才增加应用切换次数；使用时长按事件区间聚合。
- 每 5 秒形成内存实时 Snapshot，每 5 分钟批量 Upsert Activity Bucket；Lock、Sleep 和退出时立即刷新，不逐事件写 SQLite。
- 正常工作、加班、实验台和会议时间互斥归类；实验台/会议允许在无键鼠活动时累计，但 Lock/Sleep 始终暂停。
- ActivityScore 描述当前电脑操作活跃度；WorkIntensity 使用可解释权重和 EMA 平滑，含义是工作强度而非工作效率。
- Dashboard 的今日工作、连续工作、强度、键盘、鼠标、应用切换和 Sparkline 均来自真实聚合数据。
- Lock/Suspend 时刷新数据并重置连续工作，Unlock/Resume 后重新采样，午夜自动切换自然日。

完整隐私边界见 `docs\ACTIVITY_TRACKING_PRIVACY.md`，强度公式见 `docs\WORK_INTENSITY_V1.md`。

## 后续模块的资源约束

- 输入采集使用 Windows Hook/Event，不设置 10ms 轮询。
- 活动明细以内存聚合后批量事务写入。
- Dashboard 关闭或隐藏时停止 UI 刷新。
- Schedule Engine 使用下一事件唤醒模型，不进行高频轮询。
- 正式发布前需在干净 Windows 10/11 虚拟机验证：无 .NET Runtime、登录自启动、中文 TTS、单实例、移动 EXE 后注册表更新、24 小时空闲 CPU/内存与数据库增长。
