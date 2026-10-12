# English Audio Player 程序说明

## 1. 开发目的

本程序用于播放英语学习音频，并根据音频中的自然停顿位置进行快速前后跳转。

与普通播放器按固定秒数快进、快退不同，本程序会分析音频能量，识别人声之间的静音区间。当用户按前进或后退时，播放器会跳转到相邻的自然停顿位置，从而更方便地重复听某一句或跳到下一段内容。

程序不使用语音识别、AI 或复杂语音分析算法，只根据音频的能量变化判断停顿。

## 2. 主要模块

- **MainWindow**：负责主界面、用户操作、播放列表显示以及各模块之间的协调；不直接解码音频或读写 JSON。
- **AudioPlayer**：基于 NAudio 负责音频打开、播放、暂停、停止、Seek、播放位置及资源释放。
- **SilenceDetector**：读取 PCM 音频数据，计算 RMS/dBFS，识别有效停顿，并查找前后停顿位置。
- **SilenceRange**：保存一个停顿区间的开始、结束和中间位置。
- **SilenceSettings**：保存停顿检测及跳转参数、默认值和有效范围。
- **SettingsStore**：定位主程序同目录的配置文件，并负责初始化、读取、验证和保存用户配置。
- **SilenceSettingsWindow**：提供停顿参数设置、恢复默认、确定和取消操作。
- **AudioLibrary**：扫描指定文件夹中的 MP3，进行自然排序并读取标题、专辑和时长等信息。
- **AudioTrack**：表示播放列表中的一条音频及其播放状态。
主窗口允许手动缩放和系统最大化，最小尺寸为 `520 × 420`，没有应用层面的最大尺寸限制。初始宽度为屏幕工作区宽度的 50%，初始高度为工作区高度的 60%，受最小尺寸约束，居中显示。

## 3. 停顿相关参数

### 分析帧长度（FrameMilliseconds）

默认 `10 ms`。表示每次取多长的一段音频来计算声音能量。值越小分析越细，通常保持默认即可。

### 最短停顿时长（MinimumSilenceMilliseconds）

默认 `300 ms`。表示连续低能量至少持续多久才被认定为有效停顿。值越小，识别出的停顿越多；值越大，只保留较明显的停顿。

### 静音阈值（SilenceThresholdDb）

默认 `-45 dBFS`。表示声音能量低于多少时认为当前音频帧属于静音，是影响停顿识别效果的主要参数之一。

### 前后跳转容差（NavigationToleranceMilliseconds）

默认 `200 ms`。该参数不参与停顿识别，只影响前后跳转。当当前位置已经很接近某个停顿点时，用它避免再次跳回几乎相同的位置。

## 4. 基本处理流程

`MP3 → PCM → 分帧 → 计算声音能量 → 判断连续静音 → 得到停顿区间 → 前后跳转到相邻停顿中点`

整个过程不依赖字幕、语音识别或网络服务。

## 5. 用户配置

配置文件位于 exe 同一文件夹，名称为 `<主程序名>.settings.json`，默认是 `EnglishAudioPlayer.settings.json`。程序重命名后使用对应名称的配置文件。首次启动若文件不存在，自动写入四项停顿参数的默认值；以后启动读取并校验，设置窗口点击“确定”后先保存再应用。取消不保存。

保存采用同目录临时文件和文件替换。读取失败时保留原配置、暂用默认参数并在底部提示；此时不允许修改设置，修复文件或目录权限后重新启动。保存失败时保留原参数并在底部提示。配置不会转存到 AppData；程序应放在当前用户可写的文件夹。

参数输入不合法时，设置窗口内显示提示，5 秒后收起；不弹出额外消息窗口，也不保存或应用错误参数。点击“恢复默认”会清除该提示。

## 6. 单文件发布

在项目根目录执行（需要 Windows 和 .NET 10 SDK）：

```powershell
dotnet publish EnglishAudioPlayer/AudioPausePlayer.csproj -p:PublishProfile=SingleFile
```

发布配置位于 `EnglishAudioPlayer/Properties/PublishProfiles/SingleFile.pubxml`：Release、Windows x64、framework-dependent、单文件、不包含 .NET 运行时、不裁剪代码、不输出独立调试符号。默认输出目录为当前项目的 `dist/`。

等价的显式命令：

```powershell
dotnet publish EnglishAudioPlayer/AudioPausePlayer.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o dist
```

分发 `dist/EnglishAudioPlayer.exe` 即可。目标电脑必须安装 **.NET 10 Desktop Runtime x64**（普通 .NET Runtime 不足以运行 WPF）；缺少运行时时由 .NET 启动器显示安装提示，这发生在程序启动之前。官方安装入口：https://dotnet.microsoft.com/download/dotnet/10.0 。

配置文件在首次运行时自动创建，不包含在 exe 内；更新发布时保留用户已有的 `.settings.json`。如之前在 `dist/` 使用过多文件发布，只移除旧版本的程序依赖文件，不要删除用户配置或整个目录。验证发布结果时应看到单个 exe，以及可能已有的配置文件。

### 本次验证（2026-10-13）

- 34 项核心检查和 54 项 WPF 检查通过，包括配置初始化、设置窗口确认保存、再次启动读取、损坏配置保留、只读配置保存失败，以及原生窗口最大化按钮启用。
- 文档中的发布命令实际执行成功，更新发布前后配置文件 SHA256 一致；`dist/` 没有依赖 DLL 或运行时目录。
- 发布 exe 实际启动成功；将 exe 改名并放在含空格的路径后，能按新主程序名创建配置文件。
- 使用测试进程的空运行时搜索路径模拟缺少 .NET，确认启动器输出安装说明和下载链接；测试关闭了启动器的图形错误窗口，未目视验证该弹窗。
- 当前工作区没有本地 MP3，真实音频播放、音频元数据和带音频的 UI 回归本次未验证。

测试记录位于自动生成的 `docs/test-results/`（不提交到 Git）。

## 7. 构建输出与临时文件

构建和测试使用 .NET 默认的项目 `bin/`、`obj/`，不再设置额外的 `BaseOutputPath` 或使用 `artifacts/`。正式发布只输出到 `dist/`；测试报告保留在 `docs/test-results/`。

在项目根目录执行：

```powershell
dotnet build EnglishAudioPlayer/AudioPausePlayer.csproj -c Release
dotnet run --project tests/AudioPausePlayer.Tests -c Release -- --ui --playback --report
dotnet publish EnglishAudioPlayer/AudioPausePlayer.csproj -p:PublishProfile=SingleFile
```

临时验证副本放在临时目录，用完立即清理。不要为了验证额外保留完整的构建或发布副本；更新时保留 `dist/` 中的用户配置。

## 8. 模块与代码整理检查（2026-10-13）

### 设计结论

现有规模适合具体类和窗口代码直接协作，不需要 MVVM 框架、依赖注入、事件总线、通用仓储或插件机制。模块按当前职责划分，依赖关系为：主窗口调用播放器、音频目录、停顿检测、配置存储和设置窗口；这些功能模块不反向依赖主窗口。

| 模块 | 内聚性与边界 |
|---|---|
| AudioPlayer | 管理一个文件读取器、输出设备和播放生命周期；不依赖 WPF 控件、列表或停顿算法 |
| SilenceDetector / SilenceRange | 计算静音区间和导航目标；使用独立读取器，不修改播放器位置 |
| AudioLibrary / AudioTrack | 扫描、自然排序、读取元数据和保存列表项状态；不操作音频设备 |
| SilenceSettings | 集中维护四项参数的默认值和有效范围，避免各入口各自定义范围 |
| SettingsStore | 文件定位、JSON 读取和原子替换集中处理；不依赖界面 |
| SilenceSettingsWindow | 编辑和验证输入，返回确认的参数；不直接写配置或重新分析音频 |
| MainWindow | 协调输入、播放、分析、拖动、通知和关闭；保留在一个窗口中，避免碎片化拆分 |

表达式属性、空值检查、简单 lambda 和数据绑定保留。它们用于简短明确的属性或事件处理，没有引入继承层次或泛型业务框架。两个数字输入检查以及两个窗口内的短提示处理允许少量重复，避免为此增加公共抽象。

`Task`、`SemaphoreSlim` 和 `TaskCompletionSource` 用于实际异步分析、串行播放操作和等待停止事件；停止状态可能早于停止事件，不能仅凭状态立即释放资源。关闭窗口时等待操作及分析结束，再释放设备和文件读取器。没有删除这些必要的生命周期保护。

### 本次整改

- 切换音频失败时清除旧的已加载项身份。原来新文件打开失败后，旧文件身份仍被保留，导致重新点击旧文件只执行空播放器的播放操作。现在可以重新打开旧文件。
- 零 RMS 对应负无穷 dBFS，正 RMS 使用真实对数值。删除人为的 `-100 dBFS` 截断，修复阈值设为 `-100` 时漏掉完全静音的问题；保持严格小于阈值的判断。
- 配置路径定位移入 SettingsStore，减少主窗口的文件定位职责。
- 设置参数错误改为窗口内五秒提示，移除 MessageBox；删去两处与循环结果相同的冗余分支。
- 去除主窗口初始化中已无作用的最大尺寸截断，仅保留最小尺寸约束。

没有发现可以安全删除的源模块或图标资源。清理可重新生成的 `bin/`、`obj/` 和过期的清理验证日志，保留源文件、测试、当前验证记录、发布程序与用户配置。后续运行构建命令会正常重新生成必要的 `bin/`、`obj/`。

### 验证范围

35 项核心检查、58 项 WPF 检查通过。新增测试先复现静音阈值边界和音频切换失败，再验证整改；使用临时合成静音 WAV 检查真实设备打开、停止和再次加载，不写入用户音频目录。

当前没有本地 MP3，MP3 元数据、真实教材停顿效果以及完整的 MP3 播放回归仍未验证；合成 WAV 检查不代表完成教材音频的听感验收。
