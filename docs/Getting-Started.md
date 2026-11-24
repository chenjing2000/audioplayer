# Getting started

Windows x64 需要安装 [.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0)；普通 .NET Runtime 不包含 WPF，终端用户不需要 SDK。

将 `AudioPlayer.exe` 放在有写权限的目录并运行。选择包含 WAV、MP3 或 M4A 的文件夹（递归扫描子目录，跳过设置中指定名称的子目录及其内容，默认不排除任何名称；精确匹配且不区分大小写，显式选择的根目录不受限制），双击列表行播放。Space 暂停或恢复；Left/Right 跳转停顿；Ctrl+Left/Right 按配置步长后退/前进（默认 5/4 秒）；进度条支持 Seek。刷新按钮停止旧播放并递归重新扫描当前目录；扫描失败清空列表和根路径。启动时恢复 EXE 同目录的 `<实际 EXE 名>.playlist.json`，不自动播放或扫描新文件；确认缺失或已排除的路径移除，访问错误保留原 JSON 并提示。中文路径可直接阅读。

程序自动检测停顿，结果保存在音频目录下的 `results/<完整音频文件名>.silent_segments.json`。选中有可压缩静音、且检测结果有效的文件后，可用剪刀按钮输出 `<stem>.new.<ext>` 或 `<stem>.new (1).<ext>` 等不覆盖的文件。AAC/M4A 裁剪另需安装 [FFmpeg](FFmpeg-Setup.txt)；WAV/MP3 不需要。

设置保存在 EXE 同目录的 `<实际 EXE 名>.settings.json`。更新程序时保留配置、播放列表和自行安装的运行时。详见 [Configuration](Configuration.md) 和 [Publishing](Publishing.md)。开发和测试命令见 [Testing](Testing.md)。

播放模式按钮在刷新和原文按钮之间。每次点击按单曲播放 → 单曲循环 → 顺序播放 → 列表循环 → 单曲播放切换；悬停仅显示当前模式名称。仅自然结束时按模式续播，手动停止或定位至末尾不会自动继续。模式自动保存，恢复默认后确认可重置。

## 原文阅读（PR #17 待实机验收）

原文按钮在设置左侧。当前加载音频所在文件夹放置一份 ReadArticles 格式的 Script JSON（文件名不限），点击原文按钮打开伴随窗口。播放时逐字着色，字幕按相邻视觉行时间戳连续平滑滚动（自然边界除外）；滚轮按原位移的 25% 直接滚动，也可拖动或点击右侧滚动条；停止滚动约 150 ms 后按视口中心在相邻有效时间锚点间连续插值定位，支持同一行内的小幅定位。双击文本不定位。原文窗口可调整大小和关闭，但不能独立最小化/最大化；主窗口最小化时关闭原文，恢复后点击原文按钮重新打开。设置的原文页可调整显示效果。原文功能需要 Microsoft Edge WebView2 Runtime；未安装或初始化失败时音频仍可使用。原文和音频对应关系由用户保证，裁剪后不会自动修正时间戳。
