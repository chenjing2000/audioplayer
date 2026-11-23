# EnglishAudioPlayer

当前版本：**v0.1.7**

一个简单的 C# WPF 英语教材音频播放器。程序使用固定 RMS 能量阈值检测 WAV、MP3、AAC/M4A 中的持续低能量区间，左右方向键跳转到前一个或后一个停顿中点，并可对长静音进行中间裁剪。

## 技术与依赖

- .NET 10 / WPF
- NAudio 2.2.1：音频解码、播放、暂停、停止和 Seek
- TagLibSharp 2.3.0：读取音频的 Title、Album 和 Duration

程序不使用 ASR、Whisper、VAD、MVVM 框架、依赖注入、事件总线或日志框架。

## 项目结构

```text
EnglishAudioPlayer/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── SettingsStore.cs
├── AudioPlayer.cs
├── SilenceDetector.cs
├── SilenceTrimmer.cs
├── SilenceResultStore.cs
├── ToastHost.cs
├── SilenceSettings.cs
├── SilenceSettingsWindow.xaml / .cs
├── SilenceRange.cs
├── AudioLibrary.cs
├── AudioTrack.cs
└── ui/
    ├── Icons.xaml
    └── main.ico

docs/
└── program-overview.md

tests/
└── AudioPausePlayer.Tests/
```

各模块职责：

| 文件 | 职责 |
|---|---|
| `AudioPlayer.cs` | 封装 NAudio，负责打开、播放、暂停、停止、Seek、播放状态和资源释放 |
| `SilenceDetector.cs` | 独立读取 PCM，计算 RMS/dBFS，识别停顿并提供前后停顿导航 |
| `SilenceTrimmer.cs` | 读取已验证结果，在原始采样时间轴计算删除区间、淡化、编码及安全输出 |
| `SilenceResultStore.cs` | 管理唯一的 results/<完整文件名>/silence.json、元数据校验与安全替换 |
| `ToastHost.cs` | 主窗口和设置窗口共用的 WPF 悬浮消息组件 |
| `SilenceSettings.cs` | 保存停顿检测与导航参数、默认值和基本范围校验 |
| `SilenceSettingsWindow.xaml/.cs` | 编辑参数、恢复默认值，不参与音频分析 |
| `SilenceRange.cs` | 保存停顿起点、终点和中点 |
| `AudioLibrary.cs` | 扫描当前文件夹中的 WAV/MP3/M4A、自然排序并读取标签 |
| `AudioTrack.cs` | 保存列表项数据及播放/暂停显示状态 |
| `MainWindow.xaml/.cs` | WPF 布局、用户输入、后台分析和各模块协调 |
| `SettingsStore.cs` | 在 exe 同目录初始化、验证并保存用户配置 |

`MainWindow` 不处理 PCM、RMS 或 MP3 解码；`SilenceDetector` 不依赖播放器或 UI；`AudioPlayer` 不知道播放列表和停顿检测逻辑。

## 运行

开发环境需要 Windows 和 .NET 10 SDK。

```powershell
dotnet restore EnglishAudioPlayer/AudioPausePlayer.csproj
dotnet build EnglishAudioPlayer/AudioPausePlayer.csproj -c Release --no-restore
dotnet run --project EnglishAudioPlayer/AudioPausePlayer.csproj --no-restore
```

启动后点击“打开文件夹”，选择包含 WAV、MP3 或 M4A 的目录。程序只扫描该目录本身，不递归子目录。

仓库不包含测试 MP3。`.gitignore` 全局忽略 `*.mp3`，所以可以在项目根目录自行建立 `audio/` 并放入本地测试音频，不会被 Git 提交。

## 操作

- **双击列表行**：播放该音频。
- **Enter**：只有当某个表格行拥有键盘焦点时，播放该行；不是全局快捷键。
- **Space**：全局播放/暂停。尚未加载文件时播放当前选中项，没有选中项时播放第一项。
- **Left / Right**：跳到上一个 / 下一个有效停顿中点。
- **停止按钮**：停止并归零，保留当前已加载文件。
- **进度条**：点击或拖动后 Seek，保持原播放/暂停状态。
- **停止按钮右侧的剪刀按钮**：使用当前选中的音频压缩长静音，生成新的同格式音频文件。
- **剪刀按钮右侧的圆形设置按钮**：打开“设置”。仅当三项检测参数变化时重新分析；导航容差和允许静音时长改变不触发 PCM 扫描。
- **恢复默认**：将设置页中的五个参数恢复为默认值；点“确定”后应用。
- **设置错误**：在设置窗口以 Toast 提示，无效输入框显示红色边框，修正前不保存或关闭。

加载音频后后台验证 `results/<完整音频文件名>/silence.json`，命中直接复用区间，失效才扫描 PCM；左右方向键只读取已验证的内存区间。检测期间仍可切换音频或文件夹，旧分析会取消。

## 停顿检测参数

设置窗口可以修改 5 个参数。默认值：

```text
分析帧长度                 10 ms
最短停顿时长               300 ms
静音阈值                   -45 dBFS
前后跳转容差               200 ms
允许静音时长               2.0 s
```

允许范围：

```text
分析帧长度                 1 - 100 ms
最短停顿时长               50 - 5000 ms
静音阈值                   -100 - 0 dBFS
前后跳转容差               1 - 5000 ms
允许静音时长               ≥ 最短停顿时长 / 1000 (s)（显示下限向上取到 0.1 s），最多一位小数
```

前三项决定停顿识别，导航容差决定左右方向键在当前位置附近跳过多大的范围。停顿位置取静音区间中点。五项设置保存在主程序同目录的 `<主程序名>.settings.json` 中，下次启动自动读取。

原有四个整数参数输入框只接受 ASCII 数字 `0-9` 和位于首位的正负号；分析帧长度、最短停顿时长、前后跳转容差不允许负号，静音阈值允许负号。对这四个整数输入框，小数点、字母、空格和其它特殊字符均不能通过键盘输入或粘贴进入。新增的允许静音时长输入框单独允许一个英文小数点、最多三位小数；失焦、按 Enter 或确定时校验动态下限。

该算法只使用声音能量，不判断语义句界；背景音乐或持续高噪声录音可能不适用。

## 界面

- 默认宽度：屏幕工作区宽度的 50%，计算结果取整数
- 默认高度：可用屏幕高度的 60%，计算结果取整数
- 支持窗口最大化和通过边缘或角部手动调整大小
- 最小尺寸：`520 × 420`
- 没有应用层面的最大尺寸限制
- 初始尺寸按屏幕工作区约 `50% × 60%` 计算，受最小尺寸约束；尺寸和位置计算结果取整数
- 字体：12
- 三个主按钮：`36 × 36`，圆角 7
- 按钮图标：`34 × 34`，水平、垂直居中
- 剪刀按钮：位于停止按钮右侧，设置按钮左侧
- 设置按钮：位于剪刀按钮右侧，`36 × 36` 圆形按钮
- 设置窗口尺寸：`324 × 390`；5 个参数输入框宽度：`80`，数值水平、垂直居中
- 设置窗口按钮宽度：恢复默认 `76`，取消/确定 `54`
- 不显示控件焦点虚线，但保留正常键盘焦点和键盘操作

## 测试

不需要 MP3 即可运行 PCM、停顿导航、损坏文件处理等核心测试：

```powershell
dotnet restore tests/AudioPausePlayer.Tests/AudioPausePlayer.Tests.csproj
dotnet run --project tests/AudioPausePlayer.Tests --no-restore
```

如果项目根目录存在 `audio/*.mp3`，还会额外执行有效 MP3 标签测试。音频报告和播放集成测试需要本地 MP3；WPF 测试在没有 MP3 时仍检查布局、配置和窗口行为，并跳过音频相关检查：

```powershell
# 生成真实音频停顿报告到 docs/test-results/
dotnet run --project tests/AudioPausePlayer.Tests --no-restore -- --report

# 使用实际音频输出设备测试播放生命周期
dotnet run --project tests/AudioPausePlayer.Tests --no-restore -- --playback

# WPF 窗口集成测试
dotnet run --project tests/AudioPausePlayer.Tests --no-restore -- --ui
```

`docs/test-results/` 是测试生成目录，已加入 `.gitignore`。

## 发布

采用 Windows x64、framework-dependent 单文件发布，不包含 .NET 运行时：

```powershell
.\tools\Publish.ps1
```

输出为 `dist/EnglishAudioPlayer.exe`。目标电脑需要 .NET 10 Desktop Runtime x64，缺少时由 .NET 启动器提示安装。首次运行会在 exe 同目录自动创建 `<主程序名>.settings.json`，保存当前五项停顿参数；更新时保留已有配置。`dist/` 已加入 `.gitignore`。

主窗口保留最小尺寸，解除最大尺寸限制并支持系统最大化。完整发布命令、配置处理和更新方法见 [程序说明](docs/program-overview.md)。

模块设计检查、必要异步机制的理由及本轮简化记录见 [代码审查报告](docs/Code-Review-2026-10-13.md)。程序说明第 8 节保留已有整改记录；其中的历史检查数量不代表后来修改已通过验证。没有本地 MP3 时跳过教材音频相关检查。

## 统一发布与本次体检

发布入口统一为 `.\tools\Publish.ps1`，参数统一保存在主项目的 `Properties/PublishProfiles/Portable.pubxml`。详见[统一发布流程](docs/Publishing.md)、[2026-10-13 体检报告](docs/Health-Check-2026-10-13.md)和[发布说明](docs/Release-Notes.md)。


## Unified testing and acceptance

Use `.\tools\Test.ps1 -Suite Core`, `-Suite UI`, or `-Suite Full`. After publishing, run `.\tools\Test-PublishedApp.ps1`. External audio/article/dictionary paths are optional explicit arguments; missing optional integration data is reported as skipped. See the [documentation index](docs/README.md) and [test guide](docs/Testing.md). PowerShell source is English-only.

## 长静音裁剪与结果存储

选中具有有效停顿结果且存在可压缩区间的音频后，剪刀按钮可用。点击时再次验证源文件与结果；有效缓存直接供帧级裁剪使用，失效则自动检测后裁剪。音频重新编码后单独生成输出文件的 `results/<完整文件名>/silence.json`。保留 PCM 淡入淡出、源文件安全和系统编码器。

输出名为 `<stem>.lesssilence.<ext>`，重名按 ` (1)`、` (2)` 递增并跳过已占用文件，绝不覆盖；无可压缩区间时不生成副本。仅使用 WAV、MP3、AAC/M4A，不引入 FFmpeg。

本轮 [权威规格](docs/specs/Results-Cache-Toast-Refactor.md) 与 [实施验收](docs/guides/Results-Cache-Toast-Implementation.md) 已实现，并通过 Windows 真实 M4A、交互和听感验收。真实长停顿 MP3/WAV 测试按约定暂缓；详细记录见 [测试文档](docs/Testing.md)。
