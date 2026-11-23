# EnglishAudioPlayer 项目契约卡

参考：[WindowsAppPlaybook](https://github.com/chenjing2000/windowsappplaybook)。

- **用途**：Windows 英语学习音频播放器，提供基于 RMS 的静音导航与长静音压缩。
- **技术**：C#、WPF、.NET 10、NAudio 2.2.1、TagLibSharp 2.3.0；不使用 FFmpeg。
- **工程入口**：`EnglishAudioPlayer/AudioPausePlayer.csproj`；测试：`tests/AudioPausePlayer.Tests/`；发布：`tools/Publish.ps1`，正式产物 `dist/EnglishAudioPlayer.exe`。
- **运行时**：Windows x64，要求 .NET 10 Desktop Runtime；现有 framework-dependent single-file 形态保留。
- **配置**：`<实际EXE名>.settings.json` 位于 EXE 同目录，作为原有应用约定保留（区别于 Playbook 的默认 `userdata` 路径）；保存失败不覆盖旧配置。
- **音频素材**：由用户选择的外部文件夹拥有，源音频只读；长静音处理的新文件放在源文件同目录 `<stem>.lesssilence.<ext>`，旧版如已有该文件则拒绝覆盖；按 ` (1)`、` (2)` 等递增命名并始终禁止覆盖。
- **产品规则**：已完成的 [长静音规格](specs/Long-Silence-Trimming.md) 与 [实现方案](guides/Long-Silence-Implementation.md)；**已完成 Windows 验收**的 [results/Toast 重构规格](specs/Results-Cache-Toast-Refactor.md) 和 [实施方案](guides/Results-Cache-Toast-Implementation.md)；[原有程序说明](program-overview.md)。
- **行为保护**：音频播放、暂停、停止、Seek、左右方向键内存停顿导航、非递归目录扫描、设置保存与 WPF Toast 错误提示保留；每个音频只持久化一份 `results/<完整音频文件名>/silence.json`，不使用 SHA 或旧缓存兼容。
- **图标/UI 基线**：PR #4/#5 已合并；工具栏按钮点击区域 36×36、图标 28×28，墨青蓝 `#26798A` 圆底与白色图形、无外框；Play/Pause 同一按钮切换，Stop 方块高度为圆形直径 35%；程序 ICO 使用叶片标识。下一轮入口为 [HANDOFF](HANDOFF.md)。
- **测试入口**：`tools/Test.ps1 -Suite Core|UI|Full`；发布验证 `tools/Test-PublishedApp.ps1`；真实 MP3/M4A/WAV 的编解码和声音验证需在 Windows 实机执行。
- **Git**：按用户授权，在功能分支修改；未经另外授权不合并 `main`。重大验证节点以描述实际修改的英文提交消息记录。
