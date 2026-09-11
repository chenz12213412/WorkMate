# WorkMate V0.2 开发状态

## Current Phase

V0.2.1 P1 stabilization completed；S1～S5 数据正确性、并发状态转换和提醒周期修复已完成，停在 P1 Checkpoint 验证。

## Completed

- BIZ-1：新增低级键盘 Hook，只在内存累计 KeyPressCount，不读取或保存具体按键。
- BIZ-1：新增低级鼠标 Hook，统计左右中键、滚轮和相邻移动距离；鼠标坐标不持久化。
- BIZ-1：Release Build 0 Error / 0 Warning。
- BIZ-2：新增 GetLastInputInfo 采集，默认 3 分钟进入 IDLE、5 分钟进入 AFK，恢复输入后由系统时间立即回到 ACTIVE。
- BIZ-2：新增 ACTIVE / IDLE / AFK 边界自动检查，Release Build 与检查全部通过。
- BIZ-3：新增 EVENT_SYSTEM_FOREGROUND Hook；只有前台 ProcessId 变化才增加 AppSwitch。
- BIZ-3：窗口标题只保留在瞬时内存快照，AppUsage 仅按日期和进程累计前台/活跃前台时长。
- BIZ-3：AppUsage 聚合检查、Release Build 全部通过。
- BIZ-4：新增统一 5 分钟 Activity Bucket，保存计数、活动状态、应用切换、主导进程、模式和互斥工作分类。
- BIZ-4：SQLite 新增 activity_buckets 与 app_usage_daily；使用批量事务 Upsert，不逐事件写库。
- BIZ-4：跨 5 分钟与跨午夜时间片自动拆分，SQLite 回读与跨日归属检查通过。
- BIZ-5：时间片采用 LAB / MEETING、OVERTIME、NORMAL、NONE 的互斥归类，杜绝重复累计。
- BIZ-5：连续工作使用真实 Session；短 IDLE 保持 Session，AFK/午休/下班/系统不可用重置，LAB/MEETING 无输入仍可累计。
- BIZ-5：正常、加班、手动模式和 AFK 场景检查全部通过。
- BIZ-6：ActivityScore 与 WorkIntensity 分离，采用可解释权重和 alpha=0.25 EMA 平滑。
- BIZ-6：权重、归一化阈值和“强度不等于效率”说明记录于 docs/WORK_INTENSITY_V1.md。
- BIZ-6：分数边界与 EMA 非跳变检查、Release Build 全部通过。
- BIZ-7：新增 ActivitySnapshotService，5 秒汇总 Collector，5 分钟批量 Upsert Bucket/AppUsage；Lock、Sleep、Exit 时立即刷新。
- BIZ-7：Dashboard 今日工作、连续工作、强度、键盘、鼠标、应用切换及 Sparkline 已改读真实 Snapshot。
- BIZ-7：真实程序运行 16 秒稳定，生成 Activity Bucket 与 AppUsage 记录；采集失败会单项降级并写日志。
- BIZ-8：新增每日汇总，持久化正常工作、加班、实验台、会议、总工作、AFK、输入计数、平均强度、最长连续工作和主要应用。
- BIZ-8：正常下班和结束加班时生成摘要；下班后持续 ACTIVE 15 分钟只询问是否进入加班，不自动强制切换。
- BIZ-8：自动加班询问支持设置开关，并使用 ReminderHistory 保证每日去重。
- BIZ-8：DailySummary、互斥时间分类、主要应用与 SQLite 回读检查通过。
- BIZ-9：新增 Windows SessionSwitch / PowerMode 监听；Lock/Suspend 时停止采样、刷新 Bucket、清空待处理输入并重置连续工作。
- BIZ-9：只有系统已解锁且未挂起时恢复采样，避免睡眠跨夜形成超长连续工作。
- BIZ-9：喝水与站立提醒接入真实活动快照；AFK、午休、下班和暂停全部提醒时不触发，AFK 会重置站立提醒 Session。
- BIZ-9：真实输入验证中，100 次键盘输入与 20 次鼠标点击均被精确聚合到 SQLite。
- BIZ-9：新增两小时等效 5 秒采样检查，1,440 个时间片仅聚合成 24 个 5 分钟 Bucket。
- BIZ-9：前台使用时长改为基于 WinEventHook 事件区间聚合，快速切换不再把完整采样段归给最后一个进程。
- BIZ-9：最长连续工作随 Bucket 持久化，重启后每日汇总仍可取当天最大值；旧数据库会自动增加兼容字段。
- BIZ-9：内存聚合在落库后清理前一日对象，避免跨日常驻导致内存按全年持续增长。
- BIZ-9：30.5 分钟真实常驻检查：平均 CPU 0.185%，Private Memory 88.8 MB，Handle 从 748 降至 730。
- BIZ-9：发布版 `--startup` 托盘启动 30 秒：Private Memory 97.1 MB，无主窗口、无错误日志。
- 隐私审计：SQLite 只包含计数、距离、时长、状态、分数和进程名聚合字段，不存在具体按键、实际输入文字、剪贴板、鼠标坐标或轨迹字段。
- Release Build：0 Error / 0 Warning；ScheduleChecks 全部通过。
- win-x64 Self-contained SingleFile Publish 成功，`artifacts\win-x64` 仅含 `WorkMate.exe`。
- 建立语义颜色、排版、间距、圆角、卡片、快捷按钮和设置导航样式。
- 保留 Windows 原生控件行为、键盘焦点和禁用状态。
- MainWindow 改为 500×650 的单页 Dashboard，移除“今日状态 / 设置”Tab。
- 增加状态卡、2×3 指标卡和两行快捷操作。
- 新增 DashboardViewModel、IActivitySeriesProvider 和 WorkModeService。
- 新增原生 WPF ActivitySparkline，使用细线绘制 0～100 活跃度序列。
- Dashboard 3 秒统一刷新，曲线 12 秒刷新；窗口隐藏时停止计时器。
- 新增独立 SettingsWindow，使用左侧导航和七个设置分区。
- 自动启动、夏/冬令时 Profile、自动/手动切换和打扫卫生提醒均复用现有服务保存。
- 作息与提醒保存后立即由 ScheduleEngine / ReminderEngine 热加载，无需重启。
- Tray 菜单重排为今日状态、设置、快捷记录、加班/活动模式、定时暂停和退出。
- Tray 与 Dashboard 共用 WorkModeService，加班和实验台/会议模式状态不再分叉。
- 提醒暂停支持 30 分钟和 1 小时，到时自动恢复。
- Dashboard 状态图使用 IconService 按需解码并缓存的 96px IconPack 资源。
- Metrics、Actions、Navigation 分别使用 IconPack 的 6、6、7 个专用图标，不再以通用头像代替功能图标。
- WorkMate.ico 由 IconPack/App/workmate_default.png 生成，包含 8 个尺寸帧。
- Tray 图标继续由 IconService 缓存，状态优先级为提醒、加班、手动模式、作息状态。
- 旧 Generated / Source 图标路径已从正式代码和项目资源项中移除。
- IconPack 替换后 Release Build 0 Error / 0 Warning，最终 SingleFile 仅输出 WorkMate.exe。
- 完成 Visual Scale Pass：Dashboard 调整为 550×720 DIP，状态图 96 DIP，Metrics / Action 图标 24 DIP。
- Metrics 数值、Header、状态标题和 Settings 导航整体放大，Settings 导航行高为 50 DIP。
- Dashboard Footer 使用 36 DIP 齿轮按钮打开原 SettingsWindow，Hover Tooltip 为“设置”。
- MainWindow 改为首次打开时按需创建，--startup 隐藏启动不再提前构造 Dashboard 视觉树。
- 新增 UI Capture 验收工具，已生成 100% / 125% / 150% 的 Dashboard 与 SettingsWindow 截图。
- Release Build 与 ScheduleChecks：0 Error、0 Warning，全部检查通过。
- win-x64 Self-contained SingleFile Publish 成功，输出目录仅包含 WorkMate.exe。
- 单实例验证：第二次启动自动退出，系统中保持 1 个 WorkMate 进程。
- 隐藏后台 10 秒采样：CPU 0.000%，Private Memory 95.3 MB，Working Set 213.1 MB。
- Dashboard 工作强度卡片与 Sparkline Score Pill 仅显示 0～100 整数，不再显示 `/100` 或百分号。
- 新增统一 ReminderPresentationService 与 ReminderPopupWindow；喝水、站立、午休前、午休开始、下午上班、下班、打扫卫生和自动加班询问均通过同一展示链路。
- Reminder Queue 每次只显示一个窗口，优先级为 Schedule Critical、Cleaning、Stand、Drink；普通提醒之间默认间隔 3 分钟，过期提醒不补播。
- SpeechService 改为单队列播放，Schedule Critical 可停止当前普通语音，避免中文 TTS 重叠。
- 设置页新增喝水间隔、站立连续时长、各自语音/弹窗、午休三个节点、下班语音/弹窗，以及全局 TTS 开关、音量、语速和测试语音。
- `reminder_history` 自动迁移 `shown_at` 与 `action` 字段，继续承担每日去重、完成、关闭和 Snooze 记录。
- LAB 默认只保留喝水提醒；MEETING 普通提醒仅弹窗不播语音；OVERTIME 恢复普通提醒；LUNCH/OFF_WORK 停止普通提醒。
- 统一提醒弹窗截图已生成，100% / 125% / 150% UI Capture 均成功。
- 正式默认作息集中更新：夏令时 08:15～11:15、12:30～17:00；冬令时 08:15～11:15、12:00～16:45。
- Profile 默认自动切换，05-01～09-30 为夏令时，其余跨年区间为冬令时；手动 Profile 默认夏令时。
- 空数据库首次初始化、日期边界、用户保存后重启保持、自动/手动控件启停均已加入回归检查。
- MainWindow 设置齿轮改用专用 SettingsGearButtonStyle：取消默认虚线 FocusVisual、移除持续焦点边框，仅保留 Hover 与 Pressed 反馈。
- SettingsWindow 关闭时主动清理齿轮按钮焦点；其他 Button 的键盘焦点样式与访问行为不受影响。
- Dashboard 调整为 620×800 DIP，最小尺寸 600×760 DIP；正文独立 Header 已移除。
- 顶部状态卡改为 140 / * / 200 DIP 三列，整合状态图、状态说明、时间、日期与当前 Profile。
- Dashboard 保持状态、3×2 Metrics、真实强度曲线、3×2 Quick Actions、Footer 的单屏信息顺序。
- 默认 800 DIP 高度下曲线区实际约 188 DIP；为保证最小高度完整显示，Metrics 卡和快捷按钮分别使用 91 / 61 DIP 的适配高度。
- Dashboard Layout Alignment Pass 的 Debug / Release Build 均为 0 Error、0 Warning，100% / 125% / 150% 截图已生成。
- TTS 审计确认原后端为 System.Speech / SAPI；本机 SAPI 与 OneCore 暴露相同的中文声线集合，因此继续以 SAPI 作为稳定的离线默认后端。
- 新增 ISpeechEngine 与 SapiSpeechEngine，Voice 回退顺序为用户选择、zh-CN、其他中文、系统默认；初始化或合成失败只写日志，不影响主程序。
- 设置页新增真实 Voice 下拉框，默认自动选择；用户 Voice、80% 音量和“较慢 / 自然 / 较快”语速设置均可持久化并热更新。
- 新增 SpeechTextNormalizer，统一空格、标点、句尾，并将 17:00、50min、2h 49m 等转换为自然中文口语。
- 新增 SpeechTemplateProvider，为喝水、站立、午休、下午上班、下班、加班和打扫卫生提供 3～5 条温和模板，避免同类语音连续重复。
- Speech Queue 保持单通道；Lock、Sleep、禁用语音和高优先级抢占会取消当前播报，并使尚未开始的旧播报失效。
- 8 类代表性语音已使用本机 Microsoft Huihui zh-CN Voice 依次完成实际播放，未发生并发叠音。
- V0.2.1 S1：Activity Sample 跨 5 分钟或午夜时，键盘、鼠标点击、滚轮和应用切换采用 Largest Remainder 整数分配，总数严格守恒。
- V0.2.1 S2：Foreground Hook 接收 WorkMate 自身前台事件以准确结束前一个应用区间；WorkMate 不写入 AppUsage、TopApplications 或 DominantProcess。
- V0.2.1 S2：应用切换改为外部标准化 ProcessName 变化；Chrome → WorkMate → Visual Studio 只计一次外部应用切换。
- V0.2.1 S3：Tick、Lock、Unlock、Suspend、Resume、Flush 共用 Activity Semaphore；系统组合状态按事件顺序排队，只有未锁定且未挂起时恢复 Timer。
- V0.2.1 S4：站立提醒改用独立 ActiveStand 周期；完成后重新计时，AFK、午休、下班、Sleep、Lock 和 LAB 会重置，Snooze 不视为完成。
- V0.2.1 S5：ActivityScore 继续覆盖电脑活动；WorkIntensity 只对 Normal、Overtime、Lab、Meeting 加权，非工作 Bucket 不进入 Dashboard 当前值或 Sparkline。
- V0.2.1 P1 回归检查覆盖离散事件守恒、WorkMate 自身排除、外部 AppSwitch、Lock/Suspend 组合、Tick/Lock 串行化、Stand 重复周期和非工作强度隔离。
- V0.2.1 P2 H1：普通喝水/站立 Snooze 写入 `reminder_history.next_due_at`，启动后由 ReminderEngine 轮询恢复；超过宽限期的 Snooze 标记为 Expired，不依赖 Task.Delay 作为唯一触发源。
- V0.2.1 P2 H2：提醒被高优先级 Schedule Reminder 抢占时记录 Preempted，并在仍未过期时重新排队，不再误记为 Dismissed。
- V0.2.1 P2 H3：外部 AppSwitch 使用标准化 ProcessName 语义；P1 已完成并纳入回归检查。
- V0.2.1 P2 H4：WorkModeService 提供锁保护的不可变 WorkModeSnapshot，采样 Tick 使用同一快照读取 ActivityMode 与 Overtime。
- V0.2.1 P2 H5：ReminderEngine、ReminderPresentationService、SpeechService、ActivitySnapshotService 的 Dispose 改为可重复调用并避免 CTS/信号量释放竞态。
- V0.2.1 P2 H6：DatabaseStore 使用 PRAGMA user_version=3 的顺序迁移；旧 V0.2 数据保留，新增列在事务内补齐。
- V0.2.1 P2 H7：ScheduleChecks 扩展 Snooze 重启恢复、Preempted、WorkModeSnapshot、Schema Version 和双重 Dispose 回归检查。
- V0.2.1 P2 H8：新增 `.github/workflows/build.yml`，默认执行 Windows Release Build、ScheduleChecks；UI Capture 不纳入无头 CI。

## Known Issues

- Self-contained 单文件运行时的 Working Set 可能高于 100 MB；Private Memory 仍以低于 100 MB 为优先目标。
- 已完成 30.5 分钟实机资源测试和 2 小时等效聚合测试；完整 2 小时实时时钟 Soak 仍建议在发布候选机继续执行。
- GitHub Actions 需要在仓库启用后由远端 Windows Runner 执行；本地已使用相同 Release Build 与 ScheduleChecks 命令验证。

## UI Decisions

- 浅色、低对比表面、深蓝正文，强调色只用于状态和焦点。
- 不引入第三方 UI 或图表框架。
- 620×800 DIP 下优先保证全部核心区域单屏可见；使用弹性曲线区吸收窗口高度变化，避免引入滚动条。

## Next Step

V0.2.1 P2 H1～H8 已在本地完成，下一步是提交分支并在 GitHub Actions 上确认 Windows Runner 结果。
