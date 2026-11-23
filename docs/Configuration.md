# Configuration

AudioPlayer 配置文件存放在实际 EXE 同目录，默认 `AudioPlayer.settings.json`；重命名 EXE 会更改配置文件名前缀，不自动迁移旧文件。目录必须对当前用户可写。

| 参数 | 默认值 | 合法范围 |
| --- | --- | --- |
| `FrameMilliseconds` | 10 ms | 1–100 ms |
| `MinimumSilenceMilliseconds` | 300 ms | 50–5000 ms |
| `SilenceThresholdDb` | -45 dBFS | -100–0 dBFS |
| `NavigationToleranceMilliseconds` | 200 ms | 1–5000 ms |
| `AllowedSilenceSeconds` | 2.0 s | 不小于最短停顿时长/1000，最多一位小数 |

首次启动自动写入完整默认配置；读取缺字段、无效 JSON 或不合法参数时，保留文件，使用会话默认值并提示错误，用户须修复后重启才能再通过设置窗口保存。确认修改时先写同目录临时文件，再原子替换；失败保留旧配置和当前参数，不做静默修复。

设置窗口允许输入 `2`、`2.`、`2.0` 并在确认时统一显示 `2.0`；空值或非法输入保持窗口打开，显示字段错误。最短停顿 350 ms 时允许时长最低显示 0.4 s。前三项更改后必要时重新分析当前音频；仅导航容差/允许静音时长更改不会重新扫描 PCM。

这是项目明确保留的 EXE 同目录配置约定，与 WindowsAppPlaybook 默认用户目录不同。发布仅更新 EXE，不删除配置。早期三位小数配置及旧字段自动填补属于历史行为，不再支持自动迁移；历史规格保留于 Git 历史和 [文档索引](README.md)。
