# Reminder Presentation V1

WorkMate 的业务提醒统一由 `ReminderEngine` 生成请求，再交给
`ReminderPresentationService` 展示。业务引擎不直接创建窗口，也不直接调用 TTS。

## 展示链路

```text
Schedule / Activity / Cleaning
          ↓
     ReminderEngine
          ↓
ReminderPresentationService
     ├─ SpeechService
     └─ ReminderPopupWindow
```

开机问候和测试语音不是工作提醒，但仍复用同一个 `SpeechService`，因此共享启用状态、音量、语速和串行播放约束。

## 优先级与队列

一次只展示一个弹窗。队列按优先级降序、进入顺序升序选择：

1. 午休前、午休开始、下午上班、下班和自动加班询问。
2. 下班前打扫卫生。
3. 站立。
4. 喝水。

普通提醒之间保留 3 分钟最小间隔。Schedule 关键提醒不受此间隔限制；关键提醒到达时，会关闭当前普通弹窗并停止其语音，然后优先展示关键提醒。提醒携带过期时间，Lock、Sleep 或排队期间超过过期时间后会直接丢弃，恢复后不会连续补播。

## 模式规则

- `LUNCH`、`OFF_WORK`：停止普通喝水和站立提醒。
- `OVERTIME`：重新启用普通提醒，并允许使用加班语气。
- `LAB`：保留喝水，默认关闭站立提醒。
- `MEETING`：保留轻提示弹窗，普通提醒不播放语音。
- `AFK`：电脑模式下停止普通提醒，并结束当前站立提醒周期。

## 历史记录

`reminder_history` 保存提醒类型、触发时间、显示时间、用户操作、完成/延迟/关闭状态、延迟次数和下次到期时间。普通喝水和站立提醒按每次提醒生成独立类型键，不保存用户输入内容，也不需要保存弹窗正文。
