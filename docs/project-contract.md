# AudioPlayer 项目契约卡

参考：[WindowsAppPlaybook](https://github.com/chenjing2000/windowsappplaybook)。

- **用途**：Windows 英语学习音频播放器，提供基于 RMS 的静音导航与长静音压缩。
- **技术**：C#、WPF、.NET 10、NAudio 2.2.1、TagLibSharp 2.3.0；AAC/M4A 裁剪可选使用用户安装的 FFmpeg stream copy。
- **工程入口**：`AudioPlayer/AudioPlayer.csproj`；测试：`tests/AudioPlayer.Tests/`；发布：`tools/Publish.ps1`，正式产物 `dist/AudioPlayer.exe`。
- **运行时**：Windows x64，要求 .NET 10 Desktop Runtime；现有 framework-dependent single-file 形态保留。
- **配置**：`<实际EXE名>.settings.json` 位于 EXE 同目录，作为原有应用约定保留（区别于 Playbook 的默认 `userdata` 路径）；保存失败不覆盖旧配置。
- **播放列表**：EXE 同目录 `<实际EXE名>.playlist.json` 保存根目录和绝对音频路径；启动只恢复已保存且存在的文件，不自动播放；名称不显示扩展名，父目录见可换行圆角行提示，中文路径以可读 UTF-8 保存；播放列表是可重建索引，不需要自动备份。启动时确认缺失的路径才移除，访问错误保留旧 JSON 并提示；手动扫描失败保存空状态，保存失败提示且不清除内存列表。
- **音频素材**：由用户选择的外部文件夹拥有，源音频只读；长静音处理的新文件放在源文件同目录 `<stem>.new.<ext>`，如果目标文件已存在则自动避让；按 ` (1)`、` (2)` 等递增命名并始终禁止覆盖。
- **产品规则**：当前功能和输出路径以 [程序说明](program-overview.md) 为准。旧 [长静音规格](specs/Long-Silence-Trimming.md)、[结果/Toast 规格](specs/Results-Cache-Toast-Refactor.md) 与 [FFmpeg 规格](specs/FFmpeg-M4A-Stream-Copy.md) 保留设计历史，不覆盖后续已合并变更。
- **行为保护**：音频播放、暂停、停止、Seek、左右方向键内存停顿导航、递归目录扫描、设置保存与 WPF Toast 错误提示保留；每个音频只持久化一份 `results/<完整音频文件名>.silent_segments.json`，不使用 SHA 或旧缓存兼容。
- **快捷键**：主窗口 Space 播放/暂停，左右方向键静音导航，Ctrl + 左右方向键按配置步长定位（默认后退 5 s、前进 4 s，0–99 s），不依赖静音结果；设置窗口固定 480 × 420 DIP。旧配置缺少新增步长时使用默认值。
- **刷新与工具栏**：刷新会递归重新加载当前文件夹；扫描前关闭当前播放并清除原列表与文件夹路径；失败时保持空状态并提示错误，不自动恢复旧目录；扫描过滤本程序裁剪的临时音频；扫描、恢复和裁剪输出插入均使用配置的排除文件夹名称（默认空列表，精确匹配且不区分大小写，不限制显式选择的根目录）。右侧按钮间隔 5 DIP，Slider 控件居中布局并在视觉上向下偏移 10 DIP。
- **图标/UI 基线**：当前采用固定浅色界面，不承诺完整深色主题支持；PR #4/#5 已合并；工具栏按钮点击区域 36×36、图标 28×28，墨青蓝 `#26798A` 圆底与白色图形、无外框；Play/Pause 同一按钮切换，Stop 方块高度为圆形直径 35%；程序 ICO 使用叶片标识。下一轮入口为 [HANDOFF](HANDOFF.md)。
- **测试入口**：`tools/Test.ps1 -Suite Core|UI|Full`；发布验证 `tools/Test-PublishedApp.ps1`；真实 MP3/M4A/WAV 的编解码和声音验证需在 Windows 实机执行。
- **Git**：按用户授权，在功能分支修改；未经另外授权不合并 `main`。重大验证节点以描述实际修改的英文提交消息记录。
