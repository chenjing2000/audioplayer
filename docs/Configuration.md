# Configuration

AudioPlayer 配置文件存放在实际 EXE 同目录，默认 `AudioPlayer.settings.json`；重命名 EXE 会更改配置文件名前缀，不自动迁移旧文件。目录必须对当前用户可写。

| 参数 | 默认值 | 合法范围 |
| --- | --- | --- |
| `FrameMilliseconds` | 10 ms | 1–100 ms |
| `MinimumSilenceMilliseconds` | 300 ms | 50–5000 ms |
| `SilenceThresholdDb` | -45 dBFS | -100–0 dBFS |
| `NavigationToleranceMilliseconds` | 200 ms | 1–5000 ms |
| `AllowedSilenceSeconds` | 2.0 s | 不小于最短停顿时长/1000，最多一位小数 |
| `BackwardStepSeconds` | 5 s | 0–99 s |
| `ForwardStepSeconds` | 4 s | 0–99 s |
| `ExcludedFolderNames` | 空字符串 | 逗号或分号分隔的合法文件夹名称 |
| `PlaybackMode` | `PlayOne` | `PlayOne`、`RepeatOne`、`Sequential`、`RepeatAll` |

旧配置缺少新增步长、排除文件夹或播放模式字段时使用默认值；其余五项仍为必填。首次启动自动写入完整默认配置；读取缺少必填字段、无效 JSON 或不合法参数时，保留文件，使用会话默认值并提示错误，用户须修复后重启才能再通过设置窗口保存。确认修改时先写同目录临时文件，再原子替换；失败保留旧配置和当前参数，不做静默修复。

设置窗口允许输入 `2`、`2.`、`2.0` 并在确认时统一显示 `2.0`；空值或非法输入保持窗口打开，显示字段错误。最短停顿 350 ms 时允许时长最低显示 0.4 s。前三项更改后必要时重新分析当前音频；仅导航容差/允许静音时长/前后步长更改不会重新扫描 PCM。步长仅接受最多两位 ASCII 数字，确认时不能为空，0 表示不移动。

这是项目明确保留的 EXE 同目录配置约定，与 WindowsAppPlaybook 默认用户目录不同。发布仅更新 EXE，不删除配置。早期三位小数配置及旧字段自动填补属于历史行为，不再支持自动迁移；历史规格保留于 Git 历史和 [文档索引](README.md)。

排除文件夹支持中英文逗号和分号，去除首尾空白、空项及不区分大小写的重复项，确认时验证名称并用 `, ` 连接保存。默认空字符串，不排除任何名称。修改后不自动刷新或重新分析；打开文件夹、刷新、启动恢复和裁剪输出插入使用相同规则。

播放模式对应单曲播放、单曲循环、顺序播放、列表循环。点击模式按钮即时保存；设置确认保留当前模式，恢复默认后确认则重置。保存失败保留旧模式和原文件。

## 字幕参数

`ScriptColor` 表示累积式播放背景颜色，不改变正文颜色；默认浅麦黄色。播放色球使用深色对勾。

旧配置缺少 `ScriptAppearance` 或其中的字段时使用以下默认值。字幕参数与音频设置共享确认、取消、恢复默认；不提供设置内文字预览，成功保存后应用，不触发 PCM 复扫。三组颜色各五个无边框色球，单选对勾标记，悬停显示 HEX。选项卡无内容外框，选中项有主题色指示线；下拉框与音频输入框统一圆角样式、常规字重，设置窗口固定 480 × 430 DIP。

| ScriptAppearance 字段 | 默认 | 可选值 |
| --- | --- | --- |
| Font | Newsreader | Newsreader, Source Serif 4, Georgia, Microsoft YaHei UI, Nunito |
| FontSize | 17 | 10–40 DIP，整数 |
| LineHeight | 1.6 | 1–3，最多两位小数 |
| ParagraphAfter | 16 | 0–40 DIP，整数 |
| FirstLineIndent | 2 | 0–5 em，整数 |
| Color | #283747 | #283747, #344054, #3F4750, #315247, #513D4D |
| ScriptColor | #F6EDC2 | #F6EDC2, #F5E4D6, #E1EEDB, #E0ECF4, #ECE6F3 |
| Background | #F8F8F6 | #F8F8F6, #F0F3F7, #F7F1E6, #EFF5F0, #F1F0F7 |

四个数值使用圆角文本框，键盘输入和粘贴仅接受 ASCII 数字（行高可含一个英文小数点及最多两位小数）；Enter 或确定时校验非空和范围，非法值保留窗口并提示，主动确认或 Enter 校验失败时定位对应选项卡并选中错误输入框，普通失焦校验不切换选项卡；修正后清除错误。行高按 `0.##` 显示，不强行补零或截断。所有范围内旧数值原样保留。

读取时仅对移除的旧合法字体及颜色在内存中归一化：Segoe UI、Arial、Times New Roman → Newsreader；播放背景色 #A33D30/#B64550/#D71920/#F5E7A6 → #F6EDC2、#A45A2C/#BC4600/#F4D9C4 → #F5E4D6、#856300/#8A2BE2/#E5DDF2 → #ECE6F3、#53713C/#007D45/#D5E8CC → #E1EEDB、#26798A/#1685B8/#D3E5F3 → #E0ECF4；背景色 #FFFFFF → #F8F8F6。仍合法的已保存值不变，其他非法值及显式 null 仍按既有错误机制处理。读取不重写原文件；用户执行后续显式保存（例如设置确定或播放模式切换）时按现有原子保存流程写入有效参数。配置结构不变。

Newsreader、Source Serif 4 使用 Windows 已安装的字体，不随程序打包；所选字体缺失时依次尝试 Georgia 和浏览器默认衬线字体；阅读器状态文字使用 Microsoft YaHei UI 和通用 sans-serif。Segoe UI 不再作为显式显示字体，仅保留旧配置归一化。WebView2 缓存位于实际 EXE 同目录 `cache/webview2`，为可重建的运行时数据，不是原文来源。
