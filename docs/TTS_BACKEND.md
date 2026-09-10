# WorkMate 本地语音后端

## 当前选择

WorkMate V0.2 使用 `System.Speech.Synthesis`（Windows SAPI）作为默认离线语音后端，并通过 `ISpeechEngine` 隔离具体实现。

当前测试机的 SAPI 已能枚举以下中文声线：

- Microsoft Huihui Desktop（zh-CN，Female）
- Microsoft Huihui（zh-CN，Female）
- Microsoft Kangkang（zh-CN，Male）
- Microsoft Yaoyao（zh-CN，Female）

其中后三个声音也出现在本机 OneCore Voice 注册表中。对这台测试机而言，改用 WinRT `Windows.Media.SpeechSynthesis` 不会获得一组新的、更自然的中文音色。

## SAPI 与 OneCore 评估

| 维度 | System.Speech / SAPI | WinRT / OneCore |
|---|---|---|
| 中文自然度 | 取决于已安装 Voice；本机能访问 OneCore 同源中文声线 | 取决于已安装 OneCore Voice；本机音色集合与 SAPI 重合 |
| Windows 兼容性 | Win10 / Win11 桌面应用成熟稳定 | Win10 / Win11 可用，但需要 WinRT 音频流与播放链路 |
| 单 EXE | 当前包已验证 | 需要额外 Windows SDK 投影和媒体播放验证 |
| 权限与联网 | 不需要管理员权限，不上传文本 | 不需要管理员权限，本地 Voice 可离线 |
| 资源与故障面 | 依赖少，现有实现稳定 | 接口与媒体播放对象更多，生命周期更复杂 |

因此本轮不为相同音色集合引入新的 WinRT 依赖。以后若目标机器验证出 OneCore 独占的明显更自然声线，可新增 `WindowsSpeechEngine` 并保持 `SapiSpeechEngine` 自动回退。

## Voice 选择与回退

设置默认使用“自动选择”：

1. 用户指定且仍存在的 Voice；
2. `zh-CN` Voice；
3. 其他中文 Voice；
4. Windows 当前默认 Voice。

Voice 被卸载、枚举失败或单次合成失败时只记录本地日志，不影响 Schedule、Reminder、Tray 或数据采集。

## 自然度策略

- UI 语速只显示“较慢 / 自然 / 较快”，内部映射为 SAPI `-3 / -1 / 1`。
- 新用户默认音量为 80%；已有用户保存的音量不被覆盖。
- 所有文本在播放前经过 `SpeechTextNormalizer`，统一空格、标点、句尾、时间和时长表达。
- `SpeechTemplateProvider` 为每类提醒维护 3～5 条模板，并避免同一类型连续两次使用相同模板。
- Reminder Queue 仍负责业务优先级；SpeechService 使用单通道锁，确保不会同时播放两条语音。
- Lock、Sleep 或高优先级提醒抢占时，当前语音立即取消，已经排队但尚未开始的旧语音不会继续播放。
