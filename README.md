# AudioPlayer

Windows x64 英语学习音频播放器（v0.1.7），使用 C# / WPF / .NET 10、NAudio 2.2.1 与 TagLibSharp 2.3.0。

- 递归扫描选定文件夹及其子目录的 WAV、MP3、M4A（跳过名称恰为 audio 的子目录，不区分大小写；不跟随重解析点）；按相对路径自然排序，名称不显示扩展名，行提示显示父目录及读取错误，自动换行且采用圆角。
- 播放、暂停、停止、进度条 Seek；左右方向键跳转至 PCM RMS/dBFS 检测的相邻静音中点。
- 选中存在可压缩静音的音频，使用剪刀按钮生成不覆盖源文件的 `<stem>.new.<ext>`；重名依次使用 `<stem>.new (1).<ext>` 等。WAV/MP3 采用原有 PCM 裁剪，AAC/M4A 使用可选 FFmpeg `-c:a copy`，不重新编码。
- 自动复用或重建 `results/<完整音频文件名>.silent_segments.json`，静音结果按音频分别保存。不迁移或自动清理旧目录中的结果。
- 播放列表保存在 EXE 同目录的 `<实际 EXE 名>.playlist.json`，以可读 UTF-8 保存中文路径，启动时仅恢复仍存在的已保存文件，不自动播放或递归扫描；刷新重新递归扫描。
- 五项检测、导航和裁剪参数保存在 EXE 同目录的 `<实际 EXE 名>.settings.json`；无效配置不被自动覆盖。

## 使用

需安装 **.NET 10 Windows Desktop Runtime x64**（仅运行程序不需要 SDK）。从可写目录运行 `AudioPlayer.exe`，点击文件夹按钮选择音频目录。双击列表行或在聚焦的行按 Enter 播放；Space 播放/暂停；Left / Right 跳转静音中点。拖动进度条可定位；刷新会先停止当前播放并清除旧列表；扫描成功则加载新文件，扫描失败则保持列表与文件夹路径为空并提示错误，不再等待原目录恢复。

加载音频时后台验证已有静音结果，缺失或失效时重新检测。只有选中项的结果有效且存在长于允许时长的静音时，剪刀按钮才可用。输出完成后对新音频独立检测。源文件及已有输出从不覆盖。

AAC 编码的 M4A 裁剪需要用户**另行安装 FFmpeg**；普通播放、静音检测和 WAV/MP3 裁剪不需要它。请参见 [FFmpeg 安装说明](docs/FFmpeg-Setup.txt)。不支持的 M4A 编码（如 ALAC）不会静默转码。

## 开发与验证

Windows + .NET 10 SDK，在仓库根目录：

```powershell
dotnet build AudioPlayer/AudioPlayer.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```

发布结果位于 `dist/AudioPlayer.exe`。请勿直接删除 `dist/`：其中可能包含用户配置、播放列表及自行安装的 FFmpeg。详见 [Testing](docs/Testing.md)、[Publishing](docs/Publishing.md)。

详细功能与模块见 [程序说明](docs/program-overview.md)，现行配置见 [Configuration](docs/Configuration.md)，历史版本和开发交接见 [HANDOFF](docs/HANDOFF.md)。真实长静音 MP3/WAV 听感测试以及部分 FFmpeg 故障场景尚未完成验收，不能把自动化测试通过当作这些测试已完成。
