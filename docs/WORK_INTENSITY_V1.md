# WorkMate WorkIntensity V1

WorkIntensity 用于描述当前工作片段的综合强度，不表示效率，也不评价用户。

## ActivityScore

ActivityScore 表示当前电脑操作活跃程度，范围为 0～100：

- 键盘速率：45%，每分钟 120 次达到该项满分。
- 鼠标点击速率：25%，每分钟 50 次达到该项满分。
- 鼠标移动距离：20%，每分钟 15000 像素达到该项满分。
- 滚轮速率：10%，每分钟 20 次达到该项满分。
- IDLE 状态将当前操作分数乘以 0.35；AFK 为 0。

## WorkIntensity

WorkIntensity 范围为 0～100：

- 当前 ActivityScore：40%。
- 连续工作时长：25%，连续 50 分钟达到该项满分。
- 最近活动时间占比：20%。
- 应用切换活跃度：15%，每分钟 1.2 次达到该项满分。

应用切换只用于描述工作负荷，不代表效率高低。

ActivityScore 和 WorkIntensity 均使用 alpha=0.25 的指数移动平均（EMA），避免短时间内在 0 和 100 之间剧烈跳变。
