# 程序体检 - 2026-10-13

范围：发布流程、配置与路径处理、现有核心/WPF 回归和实际单文件启动；未重写业务功能。环境：Windows x64，.NET SDK 10.0.401，运行时 10.0.12。

## 结果

- 核心检查 35 项通过，WPF 检查 58 项通过，无失败。
- 发布保护检查 5 项通过：保留用户文件和配置、排除 PDB/XML、失败构建保留旧版、多文件产物拒绝、EXE 占用拒绝。
- 正式 EXE 独立复制到含空格目录并改名，从另一工作目录启动：主窗口正常，生成实际 EXE 名称对应的配置，配置没有落到工作目录。
- 使用隔离运行时目录模拟缺少 .NET，启动器给出安装说明；关闭了图形错误弹窗，未目视检查该弹窗。
- NuGet 已知漏洞检查（含传递依赖）未报告易受攻击的包；这不等于证明不存在未知漏洞。
- 正式 EXE：1,558,790 字节，约 1.49 MiB。仅分发 EXE，不含教材、用户配置或运行时。

## 检查结论与限制

配置文件名由实际进程路径确定，配置写入使用临时文件和替换；损坏配置与写入失败的保护已由现有测试覆盖。核心与窗口测试未发现本次发布改动引入的功能回归。

本地 audio/*.mp3 缺失，真实 MP3 元数据、播放生命周期和带音频的 UI 检查明确跳过，实际声音未试听。测试项目存在原有 CS4014 未等待调用警告，本次没有将其作为业务修复处理。

## 复核命令

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
dotnet run --project tests/AudioPausePlayer.Tests -c Release -- --ui
dotnet run --project tests/AudioPausePlayer.Tests -c Release -- --playback
dotnet list EnglishAudioPlayer/AudioPausePlayer.csproj package --vulnerable --include-transitive
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
```

发布配置和更新保护见 [统一发布流程](Publishing.md)。构建产物、临时验证副本和个人配置不提交到 Git。
## Unified testing and acceptance verification

Full: 39 base checks and 119 UI/playback checks passed using one representative MP3 copied from the supplied New Concept English directory. No Full groups were skipped.

Published EXE acceptance passed: isolated EXE-only folder with spaces, renamed executable, a different working directory, correct configuration placement, clean shutdown, and native missing-.NET installation instructions under an isolated runtime root. Installed runtimes and original media were not modified. This verifies automated behavior; human listening and a fresh-machine graphical runtime prompt were not performed.

The orchestration regression passed success, failure rejection, and optional-data handling checks. PowerShell source was verified as English ASCII with no syntax errors. Use [Testing](Testing.md) for the shared entry points; stage counts and individual case counts are reported separately.

## Code audit final verification

39 base checks and 119 UI/playback checks passed using the supplied representative MP3. All Full groups passed with zero failures and zero skips. The newly published portable EXE passed isolated renamed-file launch, configuration placement, clean shutdown, and missing-.NET instruction checks. See [Code Review](Code-Review-2026-10-13.md) for module assessment, actual changes and preserved complexity.

Original article files and the selected audio were verified unchanged with 370 SHA-256 comparisons. UI and real playback checks were run serially. Manual listening and a clean-machine graphical runtime prompt were not performed. Regenerable compilation and test output are cleaned after verification; dist, settings and user media are retained.
